#include "dx11-sdk-layout-probe.c"

ASSERT_SIZE(d3d11_texture2d_desc_size, D3D11_TEXTURE2D_DESC, 44);
ASSERT_ALIGNMENT(d3d11_texture2d_desc_alignment, D3D11_TEXTURE2D_DESC, 4);
ASSERT_OFFSET(d3d11_texture2d_width, D3D11_TEXTURE2D_DESC, Width, 0);
ASSERT_OFFSET(d3d11_texture2d_height, D3D11_TEXTURE2D_DESC, Height, 4);
ASSERT_OFFSET(d3d11_texture2d_mips, D3D11_TEXTURE2D_DESC, MipLevels, 8);
ASSERT_OFFSET(d3d11_texture2d_array, D3D11_TEXTURE2D_DESC, ArraySize, 12);
ASSERT_OFFSET(d3d11_texture2d_format, D3D11_TEXTURE2D_DESC, Format, 16);
ASSERT_OFFSET(d3d11_texture2d_samples, D3D11_TEXTURE2D_DESC, SampleDesc.Count, 20);
ASSERT_OFFSET(d3d11_texture2d_quality, D3D11_TEXTURE2D_DESC, SampleDesc.Quality, 24);
ASSERT_OFFSET(d3d11_texture2d_usage, D3D11_TEXTURE2D_DESC, Usage, 28);
ASSERT_OFFSET(d3d11_texture2d_bind, D3D11_TEXTURE2D_DESC, BindFlags, 32);
ASSERT_OFFSET(d3d11_texture2d_cpu, D3D11_TEXTURE2D_DESC, CPUAccessFlags, 36);
ASSERT_OFFSET(d3d11_texture2d_misc, D3D11_TEXTURE2D_DESC, MiscFlags, 40);

ASSERT_SIZE(d3d11_mapped_subresource_size, D3D11_MAPPED_SUBRESOURCE, 16);
ASSERT_ALIGNMENT(d3d11_mapped_subresource_alignment, D3D11_MAPPED_SUBRESOURCE, 8);
ASSERT_OFFSET(d3d11_mapped_data, D3D11_MAPPED_SUBRESOURCE, pData, 0);
ASSERT_OFFSET(d3d11_mapped_row_pitch, D3D11_MAPPED_SUBRESOURCE, RowPitch, 8);
ASSERT_OFFSET(d3d11_mapped_depth_pitch, D3D11_MAPPED_SUBRESOURCE, DepthPitch, 12);

ASSERT_OFFSET(d3d11_device_createtexture2d_slot, ID3D11DeviceVtbl, CreateTexture2D, 40);
ASSERT_OFFSET(d3d11_device_creatertv_slot, ID3D11DeviceVtbl, CreateRenderTargetView, 72);
ASSERT_OFFSET(d3d11_device_createpixelshader_slot, ID3D11DeviceVtbl, CreatePixelShader, 120);
ASSERT_OFFSET(d3d11_context_pssetshader_slot, ID3D11DeviceContextVtbl, PSSetShader, 72);
ASSERT_OFFSET(d3d11_context_omsetrendertargets_slot, ID3D11DeviceContextVtbl, OMSetRenderTargets, 264);
ASSERT_OFFSET(d3d11_context_map_slot, ID3D11DeviceContextVtbl, Map, 112);
ASSERT_OFFSET(d3d11_context_unmap_slot, ID3D11DeviceContextVtbl, Unmap, 120);
ASSERT_OFFSET(d3d11_context_copyresource_slot, ID3D11DeviceContextVtbl, CopyResource, 376);
ASSERT_OFFSET(d3d11_context_clearrtv_slot, ID3D11DeviceContextVtbl, ClearRenderTargetView, 400);
ASSERT_OFFSET(d3d11_texture2d_release_slot, ID3D11Texture2DVtbl, Release, 16);
ASSERT_OFFSET(d3d11_rtv_release_slot, ID3D11RenderTargetViewVtbl, Release, 16);
ASSERT_OFFSET(d3d11_pixelshader_release_slot, ID3D11PixelShaderVtbl, Release, 16);
ASSERT_SIZE(d3d11_resource_pointer_width, ID3D11Resource*, 8);
ASSERT_SIZE(d3d11_texture2d_output_width, ID3D11Texture2D**, 8);
ASSERT_SIZE(d3d11_rtv_output_width, ID3D11RenderTargetView**, 8);
ASSERT_SIZE(d3d11_pixelshader_output_width, ID3D11PixelShader**, 8);
ASSERT_SIZE(d3d11_map_type_width, D3D11_MAP, 4);
ASSERT_SIZE(d3d11_clear_channel_width, FLOAT, 4);

typedef char rgba8_unorm_format[(DXGI_FORMAT_R8G8B8A8_UNORM == 28) ? 1 : -1];
typedef char default_usage[(D3D11_USAGE_DEFAULT == 0) ? 1 : -1];
typedef char staging_usage[(D3D11_USAGE_STAGING == 3) ? 1 : -1];
typedef char render_target_bind[(D3D11_BIND_RENDER_TARGET == 0x20) ? 1 : -1];
typedef char read_access[(D3D11_CPU_ACCESS_READ == 0x20000) ? 1 : -1];
typedef char read_map[(D3D11_MAP_READ == 1) ? 1 : -1];

void NativeFfiWarpOutputSdkSignatures(const ID3D11DeviceVtbl* device,
    const ID3D11DeviceContextVtbl* context, const ID3D11Texture2DVtbl* texture,
    const ID3D11RenderTargetViewVtbl* view, const ID3D11PixelShaderVtbl* shader)
{
    HRESULT (STDMETHODCALLTYPE *createTexture)(ID3D11Device*, const D3D11_TEXTURE2D_DESC*,
        const D3D11_SUBRESOURCE_DATA*, ID3D11Texture2D**) = device->CreateTexture2D;
    HRESULT (STDMETHODCALLTYPE *createView)(ID3D11Device*, ID3D11Resource*,
        const D3D11_RENDER_TARGET_VIEW_DESC*, ID3D11RenderTargetView**) = device->CreateRenderTargetView;
    HRESULT (STDMETHODCALLTYPE *createShader)(ID3D11Device*, const void*, SIZE_T,
        ID3D11ClassLinkage*, ID3D11PixelShader**) = device->CreatePixelShader;
    void (STDMETHODCALLTYPE *setShader)(ID3D11DeviceContext*, ID3D11PixelShader*,
        ID3D11ClassInstance* const*, UINT) = context->PSSetShader;
    void (STDMETHODCALLTYPE *setTargets)(ID3D11DeviceContext*, UINT,
        ID3D11RenderTargetView* const*, ID3D11DepthStencilView*) = context->OMSetRenderTargets;
    HRESULT (STDMETHODCALLTYPE *map)(ID3D11DeviceContext*, ID3D11Resource*, UINT,
        D3D11_MAP, UINT, D3D11_MAPPED_SUBRESOURCE*) = context->Map;
    void (STDMETHODCALLTYPE *unmap)(ID3D11DeviceContext*, ID3D11Resource*, UINT) = context->Unmap;
    void (STDMETHODCALLTYPE *copy)(ID3D11DeviceContext*, ID3D11Resource*, ID3D11Resource*) =
        context->CopyResource;
    void (STDMETHODCALLTYPE *clear)(ID3D11DeviceContext*, ID3D11RenderTargetView*, const FLOAT*) =
        context->ClearRenderTargetView;
    ULONG (STDMETHODCALLTYPE *releaseTexture)(ID3D11Texture2D*) = texture->Release;
    ULONG (STDMETHODCALLTYPE *releaseView)(ID3D11RenderTargetView*) = view->Release;
    ULONG (STDMETHODCALLTYPE *releaseShader)(ID3D11PixelShader*) = shader->Release;
    (void)createTexture;
    (void)createView;
    (void)createShader;
    (void)setShader;
    (void)setTargets;
    (void)map;
    (void)unmap;
    (void)copy;
    (void)clear;
    (void)releaseTexture;
    (void)releaseView;
    (void)releaseShader;
}
