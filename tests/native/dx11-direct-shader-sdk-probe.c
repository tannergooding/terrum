#include "dx11-direct-acquisition-sdk-probe.c"
#include <d3dcompiler.h>

ASSERT_SIZE(direct_shader_extent_size, SIZE_T, 8);
ASSERT_SIZE(direct_shader_blob_cell_size, ID3DBlob*, 8);
ASSERT_ALIGNMENT(direct_shader_blob_cell_alignment, ID3DBlob*, 8);
ASSERT_OFFSET(direct_shader_blob_release_slot, ID3D10BlobVtbl, Release, 16);
ASSERT_OFFSET(direct_shader_blob_addref_slot, ID3D10BlobVtbl, AddRef, 8);
ASSERT_OFFSET(direct_shader_blob_pointer_slot, ID3D10BlobVtbl, GetBufferPointer, 24);
ASSERT_OFFSET(direct_shader_blob_size_slot, ID3D10BlobVtbl, GetBufferSize, 32);
ASSERT_OFFSET(direct_shader_vertex_create_slot, ID3D11DeviceVtbl, CreateVertexShader, 96);
ASSERT_OFFSET(direct_shader_pixel_create_slot, ID3D11DeviceVtbl, CreatePixelShader, 120);
ASSERT_OFFSET(direct_shader_input_layout_create_slot, ID3D11DeviceVtbl, CreateInputLayout, 88);
ASSERT_OFFSET(direct_shader_vertex_release_slot, ID3D11VertexShaderVtbl, Release, 16);
ASSERT_OFFSET(direct_shader_pixel_release_slot, ID3D11PixelShaderVtbl, Release, 16);
ASSERT_OFFSET(direct_shader_input_layout_release_slot, ID3D11InputLayoutVtbl, Release, 16);

void NativeFfiDirectShaderSdkSignatures(const ID3D11DeviceVtbl* device, const ID3D10BlobVtbl* blob)
{
    HRESULT (WINAPI *compile)(LPCWSTR, const D3D_SHADER_MACRO*, ID3DInclude*, LPCSTR, LPCSTR,
        UINT, UINT, ID3DBlob**, ID3DBlob**) = D3DCompileFromFile;
    HRESULT (STDMETHODCALLTYPE *vertex)(ID3D11Device*, const void*, SIZE_T, ID3D11ClassLinkage*,
        ID3D11VertexShader**) = device->CreateVertexShader;
    HRESULT (STDMETHODCALLTYPE *pixel)(ID3D11Device*, const void*, SIZE_T, ID3D11ClassLinkage*,
        ID3D11PixelShader**) = device->CreatePixelShader;
    HRESULT (STDMETHODCALLTYPE *layout)(ID3D11Device*, const D3D11_INPUT_ELEMENT_DESC*, UINT,
        const void*, SIZE_T, ID3D11InputLayout**) = device->CreateInputLayout;
    LPVOID (STDMETHODCALLTYPE *pointer)(ID3DBlob*) = blob->GetBufferPointer;
    SIZE_T (STDMETHODCALLTYPE *size)(ID3DBlob*) = blob->GetBufferSize;
    ULONG (STDMETHODCALLTYPE *addref)(ID3DBlob*) = blob->AddRef;
    ULONG (STDMETHODCALLTYPE *release)(ID3DBlob*) = blob->Release;
    (void)compile; (void)vertex; (void)pixel; (void)layout; (void)pointer; (void)size;
    (void)addref; (void)release;
}
