// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeDx11ComCatalog
{
    internal const string Unknown = "IUnknown";
    internal const string DxgiObject = "IDXGIObject";
    internal const string DxgiDeviceSubObject = "IDXGIDeviceSubObject";
    internal const string SwapChain = "IDXGISwapChain";
    internal const string Device = "ID3D11Device";
    internal const string DeviceChild = "ID3D11DeviceChild";
    internal const string DeviceContext = "ID3D11DeviceContext";
    internal const string Resource = "ID3D11Resource";
    internal const string Buffer = "ID3D11Buffer";
    internal const string Texture2D = "ID3D11Texture2D";
    internal const string View = "ID3D11View";
    internal const string RenderTargetView = "ID3D11RenderTargetView";
    internal const string InputLayout = "ID3D11InputLayout";
    internal const string VertexShader = "ID3D11VertexShader";
    internal const string PixelShader = "ID3D11PixelShader";
    internal const string Blob = "ID3D10Blob";

    internal const string CreateDeviceAndSwapChainKey =
        "windows-x64.system32.d3d11.D3D11CreateDeviceAndSwapChain";
    internal const string CompileShaderFromFileKey =
        "windows-x64.system32.d3dcompiler47.D3DCompileFromFile";
    internal const string DeviceOwnerName = "Terrum.Interop.Dx11.ProvisionalDeviceOwner";
    internal const string SwapChainOwnerName = "Terrum.Interop.Dx11.ProvisionalSwapChainOwner";
    internal const string ContextOwnerName = "Terrum.Interop.Dx11.ProvisionalContextOwner";
    internal const string BackBufferOwnerName = "Terrum.Interop.Dx11.ProvisionalBackBufferOwner";
    internal const string RenderTargetViewOwnerName = "Terrum.Interop.Dx11.ProvisionalRenderTargetViewOwner";
    internal const string InputLayoutOwnerName = "Terrum.Interop.Dx11.ProvisionalInputLayoutOwner";
    internal const string VertexShaderOwnerName = "Terrum.Interop.Dx11.ProvisionalVertexShaderOwner";
    internal const string PixelShaderOwnerName = "Terrum.Interop.Dx11.ProvisionalPixelShaderOwner";
    internal const string ShaderCodeOwnerName = "Terrum.Interop.Dx11.ProvisionalShaderCodeOwner";
    internal const string BufferOwnerName = "Terrum.Interop.Dx11.ProvisionalBufferOwner";
    internal const string BufferDescriptionTypeName = "Terrum.Interop.Dx11.BufferDesc";
    internal const string SubresourceDataTypeName = "Terrum.Interop.Dx11.SubresourceData";
    internal const string BufferBackingLoanGroup = "buffer-initial-data";
    internal const int SwapChainDescriptionSize = 72;
    internal const int SwapChainDescriptionAlignment = 8;

    internal static ImmutableArray<NativeFixedArrayStorageLayoutDefinition> FixedArrayStorageLayouts { get; } = [
        new(nameof(PrimitiveType.UInt32), 1, sizeof(uint), sizeof(uint), sizeof(uint)) {
            AllowsMutableLocal = true,
            AllowsCheckedRuntimeAccess = true,
        },
    ];

    internal static ImmutableArray<NativeStorageRecordLayoutDefinition> StorageRecordLayouts { get; } = [
        new(BufferDescriptionTypeName, "bufferdesc", 24, sizeof(uint), [
            new("ByteWidth", PrimitiveType.UInt32, 0, sizeof(uint), MinimumValue: 1),
            new("Usage", PrimitiveType.UInt32, sizeof(uint), sizeof(uint), RequiredValue: 1),
            new("BindFlags", PrimitiveType.UInt32, 2 * sizeof(uint), sizeof(uint), RequiredValue: 1),
            new("CPUAccessFlags", PrimitiveType.UInt32, 3 * sizeof(uint), sizeof(uint), RequiredValue: 0),
            new("MiscFlags", PrimitiveType.UInt32, 4 * sizeof(uint), sizeof(uint), RequiredValue: 0),
            new("StructureByteStride", PrimitiveType.UInt32, 5 * sizeof(uint), sizeof(uint), RequiredValue: 0),
        ], BackingExtentFieldIndex: 0),
        new(SubresourceDataTypeName, "subresourcedata", 16, 8, [
            new("Data", PrimitiveType.NativeStorageAddress, 0, NativeTargetProfile.PointerSize),
            new("RowPitch", PrimitiveType.UInt32, NativeTargetProfile.PointerSize, sizeof(uint),
                RequiredValue: 0),
            new("DepthPitch", PrimitiveType.UInt32, NativeTargetProfile.PointerSize + sizeof(uint), sizeof(uint),
                RequiredValue: 0),
        ], BackingPointerFieldIndex: 0),
    ];

    internal static NativeImportDefinition CreateDeviceAndSwapChainImport { get; } = new(
        CreateDeviceAndSwapChainKey, "d3d11.dll", "D3D11CreateDeviceAndSwapChain", [
            PrimitiveType.NativeNullPointer, PrimitiveType.Int32, PrimitiveType.NativeNullPointer,
            PrimitiveType.UInt32, PrimitiveType.NativeStorageAddress, PrimitiveType.UInt32,
            PrimitiveType.UInt32, PrimitiveType.NativeStorageAddress,
            PrimitiveType.NativeComInterfaceOutputAddress, PrimitiveType.NativeComInterfaceOutputAddress,
            PrimitiveType.NativeStorageAddress, PrimitiveType.NativeComInterfaceOutputAddress,
        ], PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.System32) {
        ParameterContracts = [
            NativeImportParameterContract.NullPointerOnly, NativeImportParameterContract.Value,
            NativeImportParameterContract.NullPointerOnly, NativeImportParameterContract.Value,
            NativeImportParameterContract.ReadonlyNativeStorageNoEscape, NativeImportParameterContract.Value,
            NativeImportParameterContract.Value, NativeImportParameterContract.ReadonlyNativeStorageNoEscape,
            NativeImportParameterContract.OwnedNativeComInterfaceOutput,
            NativeImportParameterContract.OwnedNativeComInterfaceOutput,
            NativeImportParameterContract.WritableNativeStorageNoEscape,
            NativeImportParameterContract.OwnedNativeComInterfaceOutput,
        ],
        StorageParameters = [
            new(4, PrimitiveType.NativeStorageAddress, PrimitiveType.NativeFixedArray, sizeof(uint),
                true, 0, nameof(PrimitiveType.UInt32), 1, sizeof(uint), sizeof(uint)),
            new(10, PrimitiveType.NativeStorageAddress, PrimitiveType.NativeFixedArray, sizeof(uint),
                false, 0, nameof(PrimitiveType.UInt32), 1, sizeof(uint), sizeof(uint)),
        ],
        AggregateStorageParameters = [
            new(7, PrimitiveType.NativeStorageAddress, SwapChainDescriptionSize,
                SwapChainDescriptionAlignment, true, SourceType: "SwapChainDescription"),
        ],
        ComOutputs = [
            new(8, SwapChain, NativeComEffects.SignedHResult
                | NativeComEffects.AcquiresReference | NativeComEffects.OutputMayBeWrittenOnFailure),
            new(9, Device, NativeComEffects.SignedHResult
                | NativeComEffects.AcquiresReference | NativeComEffects.OutputMayBeWrittenOnFailure),
            new(11, DeviceContext, NativeComEffects.SignedHResult
                | NativeComEffects.AcquiresReference | NativeComEffects.OutputMayBeWrittenOnFailure),
        ],
    };

    internal static NativeImportDefinition CompileShaderFromFileImport { get; } = new(
        CompileShaderFromFileKey, "D3DCompiler_47.dll", "D3DCompileFromFile", [
            PrimitiveType.NativeUtf16ZPointer, PrimitiveType.NativeNullPointer,
            PrimitiveType.NativeNullPointer, PrimitiveType.NativeUtf8ZPointer,
            PrimitiveType.NativeUtf8ZPointer, PrimitiveType.UInt32, PrimitiveType.UInt32,
            PrimitiveType.NativeComInterfaceOutputAddress, PrimitiveType.NativeNullPointer,
        ], PrimitiveType.Int32, true, true, false, NativeImportDeploymentPolicy.System32) {
        ParameterContracts = [
            NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
            NativeImportParameterContract.NullPointerOnly,
            NativeImportParameterContract.NullPointerOnly,
            NativeImportParameterContract.ReadonlyUtf8ZNoEscape,
            NativeImportParameterContract.ReadonlyUtf8ZNoEscape,
            NativeImportParameterContract.Value, NativeImportParameterContract.Value,
            NativeImportParameterContract.OwnedNativeComInterfaceOutput,
            NativeImportParameterContract.NullPointerOnly,
        ],
        StorageParameters = [
            new(0, PrimitiveType.NativeUtf16ZPointer, PrimitiveType.NativeFixedArray,
                NativeWindowsModulePathContract.StorageBytes, IsReadonly: true, RequiredStartIndex: 0,
                ElementTypeName: nameof(PrimitiveType.Char16),
                ElementCount: NativeWindowsModulePathContract.Capacity,
                ElementStride: sizeof(char), ElementAlignment: sizeof(char)),
        ],
        ComOutputs = [
            new(7, Blob, NativeComEffects.SignedHResult
                | NativeComEffects.AcquiresReference | NativeComEffects.OutputMayBeWrittenOnFailure),
        ],
    };

    internal static ImmutableArray<NativeAggregateStorageLayoutDefinition> AggregateStorageLayouts { get; } = [
        new("ModeDescription", 7 * sizeof(uint), sizeof(uint)) {
            Fields = [
                new("Width", nameof(PrimitiveType.UInt32), 0, sizeof(uint)),
                new("Height", nameof(PrimitiveType.UInt32), sizeof(uint), sizeof(uint)),
                new("RefreshNumerator", nameof(PrimitiveType.UInt32), 2 * sizeof(uint), sizeof(uint)),
                new("RefreshDenominator", nameof(PrimitiveType.UInt32), 3 * sizeof(uint), sizeof(uint)),
                new("Format", nameof(PrimitiveType.UInt32), 4 * sizeof(uint), sizeof(uint)),
                new("ScanlineOrdering", nameof(PrimitiveType.UInt32), 5 * sizeof(uint), sizeof(uint)),
                new("Scaling", nameof(PrimitiveType.UInt32), 6 * sizeof(uint), sizeof(uint)),
            ],
        },
        new("SampleDescription", 2 * sizeof(uint), sizeof(uint)) {
            Fields = [
                new("Count", nameof(PrimitiveType.UInt32), 0, sizeof(uint)),
                new("Quality", nameof(PrimitiveType.UInt32), sizeof(uint), sizeof(uint)),
            ],
        },
        new("SwapChainDescription", SwapChainDescriptionSize, SwapChainDescriptionAlignment) {
            Fields = [
                new("BufferDescription", "ModeDescription", 0, 7 * sizeof(uint)),
                new("SampleDescription", "SampleDescription", 7 * sizeof(uint), 2 * sizeof(uint)),
                new("BufferUsage", nameof(PrimitiveType.UInt32), 9 * sizeof(uint), sizeof(uint)),
                new("BufferCount", nameof(PrimitiveType.UInt32), 10 * sizeof(uint), sizeof(uint)),
                new("OutputWindow", nameof(PrimitiveType.NativeWindowHandle), 48, NativeTargetProfile.PointerSize),
                new("Windowed", nameof(PrimitiveType.Int32), 56, sizeof(uint)),
                new("SwapEffect", nameof(PrimitiveType.UInt32), 60, sizeof(uint)),
                new("Flags", nameof(PrimitiveType.UInt32), 64, sizeof(uint)),
            ],
        },
    ];

    internal const string GetBufferIdentity = "IDXGISwapChain::GetBuffer";
    internal const string Texture2DGetDescIdentity = "ID3D11Texture2D::GetDesc";
    internal const string CreateTexture2DIdentity = "ID3D11Device::CreateTexture2D";
    internal const string CopyResourceIdentity = "ID3D11DeviceContext::CopyResource";
    internal const string MapIdentity = "ID3D11DeviceContext::Map";
    internal const string UnmapIdentity = "ID3D11DeviceContext::Unmap";
    internal const string ResizeBuffersIdentity = "IDXGISwapChain::ResizeBuffers";
    internal const string ReleaseDeviceIdentity = "IUnknown::Release@ID3D11Device";
    internal const string ReleaseSwapChainIdentity = "IUnknown::Release@IDXGISwapChain";
    internal const string ReleaseContextIdentity = "IUnknown::Release@ID3D11DeviceContext";
    internal const string CreateRenderTargetViewIdentity = "ID3D11Device::CreateRenderTargetView";
    internal const string CreateBufferIdentity = "ID3D11Device::CreateBuffer";
    internal const string CreateInputLayoutIdentity = "ID3D11Device::CreateInputLayout";
    internal const string CreateVertexShaderIdentity = "ID3D11Device::CreateVertexShader";
    internal const string CreatePixelShaderIdentity = "ID3D11Device::CreatePixelShader";
    internal const string ReleaseTextureIdentity = "IUnknown::Release@ID3D11Texture2D";
    internal const string ReleaseRenderTargetViewIdentity = "IUnknown::Release@ID3D11RenderTargetView";
    internal const string ReleaseInputLayoutIdentity = "IUnknown::Release@ID3D11InputLayout";
    internal const string ReleaseVertexShaderIdentity = "IUnknown::Release@ID3D11VertexShader";
    internal const string ReleasePixelShaderIdentity = "IUnknown::Release@ID3D11PixelShader";
    internal const string ReleaseBufferIdentity = "IUnknown::Release@ID3D11Buffer";
    internal const string BlobAddRefIdentity = "IUnknown::AddRef@ID3D10Blob";
    internal const string BlobReleaseIdentity = "IUnknown::Release@ID3D10Blob";
    internal const string BlobGetBufferPointerIdentity = "ID3D10Blob::GetBufferPointer";
    internal const string BlobGetBufferSizeIdentity = "ID3D10Blob::GetBufferSize";
    internal const string PresentIdentity = "IDXGISwapChain::Present";
    internal const string ClearRenderTargetViewIdentity = "ID3D11DeviceContext::ClearRenderTargetView";
    internal const string OMSetRenderTargetsIdentity = "ID3D11DeviceContext::OMSetRenderTargets";
    internal const string IASetPrimitiveTopologyIdentity = "ID3D11DeviceContext::IASetPrimitiveTopology";
    internal const string IASetInputLayoutIdentity = "ID3D11DeviceContext::IASetInputLayout";
    internal const string IASetVertexBuffersIdentity = "ID3D11DeviceContext::IASetVertexBuffers";
    internal const string VSSetShaderIdentity = "ID3D11DeviceContext::VSSetShader";
    internal const string PSSetShaderIdentity = "ID3D11DeviceContext::PSSetShader";
    internal const string DrawIdentity = "ID3D11DeviceContext::Draw";
    internal const string RSSetViewportsIdentity = "ID3D11DeviceContext::RSSetViewports";
    internal const string RSGetViewportsIdentity = "ID3D11DeviceContext::RSGetViewports";
    internal const string ClearStateIdentity = "ID3D11DeviceContext::ClearState";

    internal static ImmutableArray<NativeComInterfaceDefinition> Interfaces { get; } =
        BuildInterfaces();

    internal static ImmutableArray<NativeComOwnerDefinition> Owners { get; } = [
        new(DeviceOwnerName, Device,
            NativeComOwnerAcquisitionKind.NativeImportOutput,
            CreateDeviceAndSwapChainKey, 9, true, 8, 8) {
            ReleaseMethodIdentity = ReleaseDeviceIdentity,
        },
        new(SwapChainOwnerName, SwapChain,
            NativeComOwnerAcquisitionKind.NativeImportOutput,
            CreateDeviceAndSwapChainKey, 8, true, 8, 8) {
            ReleaseMethodIdentity = ReleaseSwapChainIdentity,
        },
        new(ContextOwnerName, DeviceContext,
            NativeComOwnerAcquisitionKind.NativeImportOutput,
            CreateDeviceAndSwapChainKey, 11, true, 8, 8) {
            ReleaseMethodIdentity = ReleaseContextIdentity,
        },
        new(BackBufferOwnerName, Texture2D,
            NativeComOwnerAcquisitionKind.ComMethodOutput, GetBufferIdentity, 3, true, 8, 8) {
            AdditionalAcquisitionMethodIdentities = [CreateTexture2DIdentity],
            ReleaseMethodIdentity = ReleaseTextureIdentity,
        },
        new(RenderTargetViewOwnerName, RenderTargetView,
            NativeComOwnerAcquisitionKind.ComMethodOutput, CreateRenderTargetViewIdentity, 3, true, 8, 8) {
            ReleaseMethodIdentity = ReleaseRenderTargetViewIdentity,
        },
        new(InputLayoutOwnerName, InputLayout,
            NativeComOwnerAcquisitionKind.ComMethodOutput, CreateInputLayoutIdentity, 5, true, 8, 8) {
            ReleaseMethodIdentity = ReleaseInputLayoutIdentity,
        },
        new(VertexShaderOwnerName, VertexShader,
            NativeComOwnerAcquisitionKind.ComMethodOutput, CreateVertexShaderIdentity, 4, true, 8, 8) {
            ReleaseMethodIdentity = ReleaseVertexShaderIdentity,
        },
        new(PixelShaderOwnerName, PixelShader,
            NativeComOwnerAcquisitionKind.ComMethodOutput, CreatePixelShaderIdentity, 4, true, 8, 8) {
            ReleaseMethodIdentity = ReleasePixelShaderIdentity,
        },
        new(ShaderCodeOwnerName, Blob,
            NativeComOwnerAcquisitionKind.NativeImportOutput, CompileShaderFromFileKey,
            7, true, 8, 8) {
            AdditionalAcquisitionMethodIdentities = [BlobAddRefIdentity],
            ReleaseMethodIdentity = BlobReleaseIdentity,
        },
        new(BufferOwnerName, Buffer,
            NativeComOwnerAcquisitionKind.ComMethodOutput, CreateBufferIdentity, 3, true, 8, 8) {
            ReleaseMethodIdentity = ReleaseBufferIdentity,
        },
    ];

    internal static ImmutableArray<NativeComMethodDefinition> Methods { get; } = [
        Method(CreateBufferIdentity, Device, Device, "CreateBuffer",
            "HRESULT(ID3D11Device*,const D3D11_BUFFER_DESC*,const D3D11_SUBRESOURCE_DATA*,ID3D11Buffer**)",
            "windows-x64-rcx-rdx-r8-r9-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
                | NativeComEffects.OutputMayBeWrittenOnFailure,
            outputParameterIndex: 3, outputInterfaceIdentity: Buffer) with {
                StorageRecordParameters = [
                    new(1, BufferDescriptionTypeName, true,
                        BufferBackingLoanGroup, NoEscape: true,
                        SignatureTypeName: "D3D11_BUFFER_DESC"),
                    new(2, SubresourceDataTypeName, true,
                        BufferBackingLoanGroup, NoEscape: true,
                        SignatureTypeName: "D3D11_SUBRESOURCE_DATA"),
                ],
            },
        Method(GetBufferIdentity, SwapChain, SwapChain, "GetBuffer",
            "HRESULT(IDXGISwapChain*,UINT,REFIID,void**)",
            "windows-x64-rcx-edx-r8-r9-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
                | NativeComEffects.OutputMayBeWrittenOnFailure,
            outputParameterIndex: 3, outputInterfaceIdentity: Texture2D) with {
                HiddenParameters = [
                    new(1, NativeComHiddenParameterKind.UInt32),
                    new(2, NativeComHiddenParameterKind.InterfaceIdAddress, InterfaceIdentity: Texture2D),
                ],
            },
        Method(Texture2DGetDescIdentity, Texture2D, Texture2D, "GetDesc",
            "void(ID3D11Texture2D*,D3D11_TEXTURE2D_DESC*)",
            "windows-x64-rcx-rdx-void", NativeComEffects.None,
            parameterAdaptations: [
                new(1, "D3D11_TEXTURE2D_DESC", "D3D11_TEXTURE2D_DESC*",
                    NativeComParameterAdaptationKind.AggregateStorageAddress, 44, 4, NoEscape: true),
            ]),
        Method(CreateTexture2DIdentity, Device, Device, "CreateTexture2D",
            "HRESULT(ID3D11Device*,const D3D11_TEXTURE2D_DESC*,const D3D11_SUBRESOURCE_DATA*,ID3D11Texture2D**)",
            "windows-x64-rcx-rdx-r8-r9-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
                | NativeComEffects.OutputMayBeWrittenOnFailure,
            outputParameterIndex: 3, outputInterfaceIdentity: Texture2D,
            parameterAdaptations: [
                new(1, "D3D11_TEXTURE2D_DESC", "const D3D11_TEXTURE2D_DESC*",
                    NativeComParameterAdaptationKind.AggregateAddress, 44, 4),
            ]) with {
                BoundMethodName = "TryCreateTexture2D",
                HiddenParameters = [new(2, NativeComHiddenParameterKind.NullPointer)],
            },
        Method(CopyResourceIdentity, DeviceContext, DeviceContext, "CopyResource",
            "void(ID3D11DeviceContext*,ID3D11Resource*,ID3D11Resource*)",
            "windows-x64-rcx-rdx-r8-void", NativeComEffects.None,
            parameterAdaptations: [
                new(1, "ID3D11Texture2D*", "ID3D11Resource*",
                    NativeComParameterAdaptationKind.PointerPreservingBaseInterface),
                new(2, "ID3D11Texture2D*", "ID3D11Resource*",
                    NativeComParameterAdaptationKind.PointerPreservingBaseInterface),
            ]) with {
                BorrowedOwnerParameters = [1, 2],
            },
        Method(MapIdentity, DeviceContext, DeviceContext, "Map",
            "HRESULT(ID3D11DeviceContext*,ID3D11Resource*,UINT,D3D11_MAP,UINT,D3D11_MAPPED_SUBRESOURCE*)",
            "windows-x64-rcx-rdx-r8d-r9d-stack32-stack40-eax", NativeComEffects.SignedHResult,
            parameterAdaptations: [
                new(1, "ID3D11Texture2D*", "ID3D11Resource*",
                    NativeComParameterAdaptationKind.PointerPreservingBaseInterface),
                new(5, "D3D11_MAPPED_SUBRESOURCE", "D3D11_MAPPED_SUBRESOURCE*",
                    NativeComParameterAdaptationKind.AggregateStorageAddress, 16, 8, NoEscape: true),
            ]) with {
                BorrowedOwnerParameters = [1],
                MappedBufferOutput = new(5, "D3D11_MAPPED_SUBRESOURCE", 0, 12, UnmapIdentity) {
                    ReadOnlyMapArguments = [new(3, 1), new(4, 0)],
                },
            },
        Method(UnmapIdentity, DeviceContext, DeviceContext, "Unmap",
            "void(ID3D11DeviceContext*,ID3D11Resource*,UINT)",
            "windows-x64-rcx-rdx-r8d-void", NativeComEffects.None,
            parameterAdaptations: [
                new(1, "ID3D11Texture2D*", "ID3D11Resource*",
                    NativeComParameterAdaptationKind.PointerPreservingBaseInterface),
            ]) with {
                BorrowedOwnerParameters = [1],
            },
        Method(ResizeBuffersIdentity, SwapChain, SwapChain, "ResizeBuffers",
            "HRESULT(IDXGISwapChain*,UINT,UINT,UINT,DXGI_FORMAT,UINT)",
            "windows-x64-rcx-edx-r8d-r9d-stack32-stack40-eax",
            NativeComEffects.SignedHResult),
        Method(CreateRenderTargetViewIdentity, Device, Device, "CreateRenderTargetView",
            "HRESULT(ID3D11Device*,ID3D11Resource*,const D3D11_RENDER_TARGET_VIEW_DESC*,ID3D11RenderTargetView**)",
            "windows-x64-rcx-rdx-r8-r9-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
                | NativeComEffects.OutputMayBeWrittenOnFailure,
            outputParameterIndex: 3, outputInterfaceIdentity: RenderTargetView,
            parameterAdaptations: [
                new(1, "ID3D11Texture2D*", "ID3D11Resource*",
                    NativeComParameterAdaptationKind.PointerPreservingBaseInterface),
            ]) with {
                BorrowedOwnerParameters = [1],
                HiddenParameters = [new(2, NativeComHiddenParameterKind.NullPointer)],
            },
        Method(CreateInputLayoutIdentity, Device, Device, "CreateInputLayout",
            "HRESULT(ID3D11Device*,const D3D11_INPUT_ELEMENT_DESC*,UINT,const void*,SIZE_T,ID3D11InputLayout**)",
            "windows-x64-rcx-rdx-r8d-r9-stack32-stack40-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
                | NativeComEffects.OutputMayBeWrittenOnFailure,
            outputParameterIndex: 5, outputInterfaceIdentity: InputLayout,
            parameterAdaptations: [
                new(3, "ID3D10Blob*", "const void*",
                    NativeComParameterAdaptationKind.ComObjectBufferContents,
                    BufferPointerMethodIdentity: BlobGetBufferPointerIdentity,
                    BufferSizeMethodIdentity: BlobGetBufferSizeIdentity),
            ]) with {
                BorrowedOwnerParameters = [3],
            },
        Method(CreateVertexShaderIdentity, Device, Device, "CreateVertexShader",
            "HRESULT(ID3D11Device*,const void*,SIZE_T,ID3D11ClassLinkage*,ID3D11VertexShader**)",
            "windows-x64-rcx-rdx-r8-r9-stack32-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
                | NativeComEffects.OutputMayBeWrittenOnFailure,
            outputParameterIndex: 4, outputInterfaceIdentity: VertexShader,
            parameterAdaptations: [
                new(1, "ID3D10Blob*", "const void*",
                    NativeComParameterAdaptationKind.ComObjectBufferContents,
                    BufferPointerMethodIdentity: BlobGetBufferPointerIdentity,
                    BufferSizeMethodIdentity: BlobGetBufferSizeIdentity),
            ]) with {
                BorrowedOwnerParameters = [1],
                HiddenParameters = [new(3, NativeComHiddenParameterKind.NullPointer)],
            },
        Method(CreatePixelShaderIdentity, Device, Device, "CreatePixelShader",
            "HRESULT(ID3D11Device*,const void*,SIZE_T,ID3D11ClassLinkage*,ID3D11PixelShader**)",
            "windows-x64-rcx-rdx-r8-r9-stack32-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
                | NativeComEffects.OutputMayBeWrittenOnFailure,
            outputParameterIndex: 4, outputInterfaceIdentity: PixelShader,
            parameterAdaptations: [
                new(1, "ID3D10Blob*", "const void*",
                    NativeComParameterAdaptationKind.ComObjectBufferContents,
                    BufferPointerMethodIdentity: BlobGetBufferPointerIdentity,
                    BufferSizeMethodIdentity: BlobGetBufferSizeIdentity),
            ]) with {
                BorrowedOwnerParameters = [1],
                HiddenParameters = [new(3, NativeComHiddenParameterKind.NullPointer)],
            },
        Method(ReleaseTextureIdentity, Texture2D, Unknown, "Release",
            "ULONG(ID3D11Texture2D*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference),
        Method(ReleaseRenderTargetViewIdentity, RenderTargetView, Unknown, "Release",
            "ULONG(ID3D11RenderTargetView*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference),
        Method(ReleaseInputLayoutIdentity, InputLayout, Unknown, "Release",
            "ULONG(ID3D11InputLayout*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference),
        Method(ReleaseVertexShaderIdentity, VertexShader, Unknown, "Release",
            "ULONG(ID3D11VertexShader*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference),
        Method(ReleasePixelShaderIdentity, PixelShader, Unknown, "Release",
            "ULONG(ID3D11PixelShader*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference),
        Method(ReleaseBufferIdentity, Buffer, Unknown, "Release",
            "ULONG(ID3D11Buffer*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference),
        Method(ReleaseDeviceIdentity, Device, Unknown, "Release",
            "ULONG(ID3D11Device*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference),
        Method(ReleaseSwapChainIdentity, SwapChain, Unknown, "Release",
            "ULONG(IDXGISwapChain*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference),
        Method(ReleaseContextIdentity, DeviceContext, Unknown, "Release",
            "ULONG(ID3D11DeviceContext*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference),
        Method(BlobAddRefIdentity, Blob, Unknown, "AddRef",
            "ULONG(ID3DBlob*)", "windows-x64-rcx-eax",
            NativeComEffects.AcquiresReference),
        Method(BlobReleaseIdentity, Blob, Unknown, "Release",
            "ULONG(ID3DBlob*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference),
        Method(BlobGetBufferPointerIdentity, Blob, Blob, "GetBufferPointer",
            "LPVOID(ID3DBlob*)", "windows-x64-rcx-rax",
            NativeComEffects.None),
        Method(BlobGetBufferSizeIdentity, Blob, Blob, "GetBufferSize",
            "SIZE_T(ID3DBlob*)", "windows-x64-rcx-rax",
            NativeComEffects.None),
        Method(PresentIdentity, SwapChain, SwapChain, "Present",
            "HRESULT(IDXGISwapChain*,UINT,UINT)", "windows-x64-rcx-edx-r8d-eax",
            NativeComEffects.SignedHResult) with {
                HiddenParameters = [
                    new(1, NativeComHiddenParameterKind.UInt32, 1),
                    new(2, NativeComHiddenParameterKind.UInt32),
                ],
            },
        Method(ClearRenderTargetViewIdentity, DeviceContext, DeviceContext, "ClearRenderTargetView",
            "void(ID3D11DeviceContext*,ID3D11RenderTargetView*,const FLOAT[4])",
            "windows-x64-rcx-rdx-r8-disp32", NativeComEffects.None,
            parameterAdaptations: [
                new(2, "float", "const FLOAT[4]",
                    NativeComParameterAdaptationKind.ValueArrayAddress),
            ]) with {
                BorrowedOwnerParameters = [1],
            },
        Method(OMSetRenderTargetsIdentity, DeviceContext, DeviceContext, "OMSetRenderTargets",
            "void(ID3D11DeviceContext*,UINT,ID3D11RenderTargetView*const*,ID3D11DepthStencilView*)",
            "windows-x64-rcx-edx-r8-r9-void", NativeComEffects.None,
            parameterAdaptations: [
                new(2, "ID3D11RenderTargetView*", "ID3D11RenderTargetView*const*",
                    NativeComParameterAdaptationKind.SingleElementOwnerPointerArray),
            ]) with {
                BorrowedOwnerParameters = [2],
                HiddenParameters = [
                    new(1, NativeComHiddenParameterKind.UInt32, 1),
                    new(3, NativeComHiddenParameterKind.NullPointer),
                ],
            },
        Method(IASetPrimitiveTopologyIdentity, DeviceContext, DeviceContext, "IASetPrimitiveTopology",
            "void(ID3D11DeviceContext*,D3D11_PRIMITIVE_TOPOLOGY)",
            "windows-x64-rcx-edx-void", NativeComEffects.None),
        Method(IASetInputLayoutIdentity, DeviceContext, DeviceContext, "IASetInputLayout",
            "void(ID3D11DeviceContext*,ID3D11InputLayout*)",
            "windows-x64-rcx-rdx-void", NativeComEffects.None) with {
            BorrowedOwnerParameters = [1],
        },
        Method(IASetVertexBuffersIdentity, DeviceContext, DeviceContext, "IASetVertexBuffers",
            "void(ID3D11DeviceContext*,UINT,UINT,ID3D11Buffer*const*,const UINT*,const UINT*)",
            "windows-x64-rcx-edx-r8d-r9-stack32-stack40-void", NativeComEffects.None,
            parameterAdaptations: [
                new(3, "ID3D11Buffer*", "ID3D11Buffer*const*",
                    NativeComParameterAdaptationKind.SingleElementOwnerPointerArray),
                new(4, "uint32", "const UINT*", NativeComParameterAdaptationKind.ValueAddress),
                new(5, "uint32", "const UINT*", NativeComParameterAdaptationKind.ValueAddress),
            ]) with {
                BorrowedOwnerParameters = [3],
                HiddenParameters = [
                    new(1, NativeComHiddenParameterKind.UInt32),
                    new(2, NativeComHiddenParameterKind.UInt32, 1),
                ],
            },
        Method(VSSetShaderIdentity, DeviceContext, DeviceContext, "VSSetShader",
            "void(ID3D11DeviceContext*,ID3D11VertexShader*,ID3D11ClassInstance*const*,UINT)",
            "windows-x64-rcx-rdx-r8-r9d-void", NativeComEffects.None) with {
                BorrowedOwnerParameters = [1],
                HiddenParameters = [
                    new(2, NativeComHiddenParameterKind.NullPointer),
                    new(3, NativeComHiddenParameterKind.UInt32),
                ],
            },
        Method(PSSetShaderIdentity, DeviceContext, DeviceContext, "PSSetShader",
            "void(ID3D11DeviceContext*,ID3D11PixelShader*,ID3D11ClassInstance*const*,UINT)",
            "windows-x64-rcx-rdx-r8-r9d-void", NativeComEffects.None) with {
                BorrowedOwnerParameters = [1],
                HiddenParameters = [
                    new(2, NativeComHiddenParameterKind.NullPointer),
                    new(3, NativeComHiddenParameterKind.UInt32),
                ],
            },
        Method(DrawIdentity, DeviceContext, DeviceContext, "Draw",
            "void(ID3D11DeviceContext*,UINT,UINT)",
            "windows-x64-rcx-edx-r8d-void", NativeComEffects.None),
        // A D3D11_VIEWPORT is six contiguous FLOAT fields, so one viewport has the same
        // address and layout as this scalar-array view used by the generic value-array adapter.
        Method(RSSetViewportsIdentity, DeviceContext, DeviceContext, "RSSetViewports",
            "void(ID3D11DeviceContext*,UINT,const FLOAT[6])",
            "windows-x64-rcx-edx-r8-void", NativeComEffects.None,
            parameterAdaptations: [
                new(2, "float", "const FLOAT[6]",
                    NativeComParameterAdaptationKind.ValueArrayAddress),
            ]) with {
                HiddenParameters = [new(1, NativeComHiddenParameterKind.UInt32, 1)],
            },
        Method(RSGetViewportsIdentity, DeviceContext, DeviceContext, "RSGetViewports",
            "void(ID3D11DeviceContext*,UINT*,D3D11_VIEWPORT*)",
            "windows-x64-rcx-rdx-r8-void", NativeComEffects.None,
            parameterAdaptations: [
                new(1, "uint32", "UINT*", NativeComParameterAdaptationKind.ValueOutputAddress,
                    NoEscape: true),
                new(2, "D3D11_VIEWPORT", "D3D11_VIEWPORT*",
                    NativeComParameterAdaptationKind.AggregateStorageAddress, 24, 4, NoEscape: true),
            ]),
        Method(ClearStateIdentity, DeviceContext, DeviceContext, "ClearState",
            "void(ID3D11DeviceContext*)",
            "windows-x64-rcx-void", NativeComEffects.None),
    ];

    internal static ImmutableArray<(string Source, string Target)> PermittedBaseEdges { get; } = [
        (Texture2D, Resource),
    ];

    internal static ImmutableArray<NativeComSignatureTypeDefinition> SignatureTypes { get; } = [
        new("DXGI_FORMAT", NativeComAbiValueKind.Unsigned32),
        new("D3D11_MAP", NativeComAbiValueKind.Unsigned32),
        new("D3D11_PRIMITIVE_TOPOLOGY", NativeComAbiValueKind.Unsigned32),
    ];

    internal static bool MatchesCatalog(NativeImportCatalog? catalog) =>
        catalog is { IsForTesting: false, Identity: NativeWindowCreationContract.CatalogIdentity }
        && Interfaces.SequenceEqual(catalog.ComInterfaces)
        && Owners.SequenceEqual(catalog.ComOwners)
        && Methods.SequenceEqual(catalog.ComMethods)
        && SignatureTypes.SequenceEqual(catalog.ComSignatureTypes)
        && PermittedBaseEdges.SequenceEqual(catalog.ComBaseEdges);

    internal static NativeComInterfaceDefinition Interface(string identity) =>
        Interfaces.Single(definition => definition.NominalIdentity == identity);

    internal static NativeComOwnerDefinition Owner(string boundOwnerName) =>
        Owners.Single(definition => definition.BoundOwnerName == boundOwnerName);

    internal static NativeComMethodDefinition Method(string identity) =>
        Methods.Single(definition => definition.Identity == identity);

    internal static bool IsPermittedBaseEdge(string source, string target) =>
        PermittedBaseEdges.Contains((source, target))
        && Interface(source).ImmediateBaseIdentity == target;

    internal static int Slot(string interfaceIdentity, string methodIdentity) =>
        Interface(interfaceIdentity).FlattenedMethods.IndexOf(methodIdentity);

    private static NativeComMethodDefinition Method(string identity, string owner, string declaring,
        string method, string signature, string abi, NativeComEffects effects,
        int outputParameterIndex = -1,
        string? outputInterfaceIdentity = null,
        ImmutableArray<NativeComParameterAdaptation> parameterAdaptations = default) =>
        new(identity, owner, declaring, Slot(owner, declaring + "::" + method), signature, abi,
            effects, true, outputParameterIndex, outputInterfaceIdentity, parameterAdaptations) {
            VTableMethodIdentity = declaring + "::" + method,
        };

    private static ImmutableArray<NativeComInterfaceDefinition> BuildInterfaces()
    {
        var interfaces = ImmutableArray.CreateBuilder<NativeComInterfaceDefinition>(16);

        void Add(string identity, string iid, string? baseIdentity, params string[] ownMethods)
        {
            var inherited = baseIdentity is null
                ? []
                : interfaces.Single(definition => definition.NominalIdentity == baseIdentity).FlattenedMethods;
            var declared = ownMethods.Select(method => identity + "::" + method).ToImmutableArray();
            interfaces.Add(new NativeComInterfaceDefinition(identity, Guid.Parse(iid), baseIdentity,
                [.. inherited, .. declared], declared));
        }

        Add(Unknown, "00000000-0000-0000-C000-000000000046", null,
            "QueryInterface", "AddRef", "Release");
        Add(DxgiObject, "AEC22FB8-76F3-4639-9BE0-28EB43A67A2E", Unknown,
            "SetPrivateData", "SetPrivateDataInterface", "GetPrivateData", "GetParent");
        Add(DxgiDeviceSubObject, "3D3E0379-F9DE-4D58-BB6C-18D62992F1A6", DxgiObject,
            "GetDevice");
        Add(SwapChain, "310D36A0-D2E7-4C0A-AA04-6A9D23B8886A", DxgiDeviceSubObject,
            "Present", "GetBuffer", "SetFullscreenState", "GetFullscreenState", "GetDesc",
            "ResizeBuffers", "ResizeTarget", "GetContainingOutput", "GetFrameStatistics",
            "GetLastPresentCount");
        Add(Device, "DB6F6DDB-AC77-4E88-8253-819DF9BBF140", Unknown,
            "CreateBuffer", "CreateTexture1D", "CreateTexture2D", "CreateTexture3D",
            "CreateShaderResourceView", "CreateUnorderedAccessView", "CreateRenderTargetView",
            "CreateDepthStencilView", "CreateInputLayout", "CreateVertexShader",
            "CreateGeometryShader", "CreateGeometryShaderWithStreamOutput", "CreatePixelShader",
            "CreateHullShader", "CreateDomainShader", "CreateComputeShader", "CreateClassLinkage",
            "CreateBlendState", "CreateDepthStencilState", "CreateRasterizerState", "CreateSamplerState",
            "CreateQuery", "CreatePredicate", "CreateCounter", "CreateDeferredContext",
            "OpenSharedResource", "CheckFormatSupport", "CheckMultisampleQualityLevels",
            "CheckCounterInfo", "CheckCounter", "CheckFeatureSupport", "GetPrivateData",
            "SetPrivateData", "SetPrivateDataInterface", "GetFeatureLevel", "GetCreationFlags",
            "GetDeviceRemovedReason", "GetImmediateContext", "SetExceptionMode", "GetExceptionMode");
        Add(DeviceChild, "1841E5C8-16B0-489B-BCC8-44CFB0D5DEAE", Unknown,
            "GetDevice", "GetPrivateData", "SetPrivateData", "SetPrivateDataInterface");
        Add(DeviceContext, "C0BFA96C-E089-44FB-8EAF-26F8796190DA", DeviceChild,
            "VSSetConstantBuffers", "PSSetShaderResources", "PSSetShader", "PSSetSamplers", "VSSetShader",
            "DrawIndexed", "Draw", "Map", "Unmap", "PSSetConstantBuffers", "IASetInputLayout",
            "IASetVertexBuffers", "IASetIndexBuffer", "DrawIndexedInstanced", "DrawInstanced",
            "GSSetConstantBuffers", "GSSetShader", "IASetPrimitiveTopology", "VSSetShaderResources",
            "VSSetSamplers", "Begin", "End", "GetData", "SetPredication",
            "GSSetShaderResources", "GSSetSamplers", "OMSetRenderTargets",
            "OMSetRenderTargetsAndUnorderedAccessViews", "OMSetBlendState", "OMSetDepthStencilState",
            "SOSetTargets", "DrawAuto", "DrawIndexedInstancedIndirect", "DrawInstancedIndirect", "Dispatch",
            "DispatchIndirect", "RSSetState", "RSSetViewports", "RSSetScissorRects", "CopySubresourceRegion",
            "CopyResource", "UpdateSubresource", "CopyStructureCount", "ClearRenderTargetView",
            "ClearUnorderedAccessViewUint", "ClearUnorderedAccessViewFloat", "ClearDepthStencilView",
            "GenerateMips", "SetResourceMinLOD", "GetResourceMinLOD", "ResolveSubresource",
            "ExecuteCommandList", "HSSetShaderResources", "HSSetShader", "HSSetSamplers",
            "HSSetConstantBuffers", "DSSetShaderResources", "DSSetShader", "DSSetSamplers",
            "DSSetConstantBuffers", "CSSetShaderResources", "CSSetUnorderedAccessViews",
            "CSSetShader", "CSSetSamplers", "CSSetConstantBuffers", "VSGetConstantBuffers",
            "PSGetShaderResources", "PSGetShader", "PSGetSamplers", "VSGetShader",
            "PSGetConstantBuffers", "IAGetInputLayout", "IAGetVertexBuffers",
            "IAGetIndexBuffer", "GSGetConstantBuffers", "GSGetShader",
            "IAGetPrimitiveTopology", "VSGetShaderResources", "VSGetSamplers",
            "GetPredication", "GSGetShaderResources", "GSGetSamplers", "OMGetRenderTargets",
            "OMGetRenderTargetsAndUnorderedAccessViews", "OMGetBlendState",
            "OMGetDepthStencilState", "SOGetTargets", "RSGetState", "RSGetViewports",
            "RSGetScissorRects", "HSGetShaderResources", "HSGetShader", "HSGetSamplers",
            "HSGetConstantBuffers", "DSGetShaderResources", "DSGetShader", "DSGetSamplers",
            "DSGetConstantBuffers", "CSGetShaderResources", "CSGetUnorderedAccessViews",
            "CSGetShader", "CSGetSamplers", "CSGetConstantBuffers", "ClearState");
        Add(Resource, "DC8E63F3-D12B-4952-B47B-5E45026A862D", DeviceChild,
            "GetType", "SetEvictionPriority", "GetEvictionPriority");
        Add(Buffer, "48570B85-D1EE-4FCD-A250-EB350722B037", Resource,
            "GetDesc");
        Add(Texture2D, "6F15AAF2-D208-4E89-9AB4-489535D34F9C", Resource,
            "GetDesc");
        Add(View, "839D1216-BB2E-412B-B7F4-A9DBEBE08ED1", DeviceChild,
            "GetResource");
        Add(RenderTargetView, "DFDBA067-0B8D-4865-875B-D7B4516CC164", View,
            "GetDesc");
        Add(InputLayout, "E4819DDC-4CF0-4025-BD26-5DE82A3E07B7", DeviceChild);
        Add(VertexShader, "3B301D64-D678-4289-8897-22F8928B72F3", DeviceChild);
        Add(PixelShader, "EA82E40D-51DC-4F33-93D4-DB7C9125AE8C", DeviceChild);
        Add(Blob, "8BA5FB08-5195-40E2-AC58-0D989C3A0102", Unknown,
            "GetBufferPointer", "GetBufferSize");
        interfaces[^1] = interfaces[^1] with { SignatureAliases = ["ID3DBlob"] };
        return interfaces.ToImmutable();
    }
}
