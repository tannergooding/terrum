#include "dx11-sdk-layout-probe.c"

ASSERT_SIZE(direct_hresult_size, HRESULT, 4);
ASSERT_ALIGNMENT(direct_hresult_alignment, HRESULT, 4);
ASSERT_SIZE(direct_driver_size, D3D_DRIVER_TYPE, 4);
ASSERT_ALIGNMENT(direct_driver_alignment, D3D_DRIVER_TYPE, 4);
ASSERT_SIZE(direct_feature_level_size, D3D_FEATURE_LEVEL, 4);
ASSERT_ALIGNMENT(direct_feature_level_alignment, D3D_FEATURE_LEVEL, 4);
ASSERT_SIZE(direct_device_cell_size, ID3D11Device*, 8);
ASSERT_SIZE(direct_context_cell_size, ID3D11DeviceContext*, 8);
ASSERT_SIZE(direct_swap_cell_size, IDXGISwapChain*, 8);
ASSERT_ALIGNMENT(direct_device_cell_alignment, ID3D11Device*, 8);
ASSERT_ALIGNMENT(direct_context_cell_alignment, ID3D11DeviceContext*, 8);
ASSERT_ALIGNMENT(direct_swap_cell_alignment, IDXGISwapChain*, 8);
ASSERT_OFFSET(direct_device_release_slot, ID3D11DeviceVtbl, Release, 16);
ASSERT_OFFSET(direct_context_release_slot, ID3D11DeviceContextVtbl, Release, 16);
ASSERT_OFFSET(direct_swap_release_slot, IDXGISwapChainVtbl, Release, 16);
typedef char direct_hresult_signed[((HRESULT)-1 < 0) ? 1 : -1];
typedef char direct_driver_signed[((D3D_DRIVER_TYPE)-1 < 0) ? 1 : -1];
typedef char direct_warp_value[(D3D_DRIVER_TYPE_WARP == 5) ? 1 : -1];
typedef char direct_hardware_value[(D3D_DRIVER_TYPE_HARDWARE == 1) ? 1 : -1];
typedef char direct_feature_value[(D3D_FEATURE_LEVEL_11_0 == 0xB000) ? 1 : -1];
typedef char direct_sdk_value[(D3D11_SDK_VERSION == 7) ? 1 : -1];
typedef char direct_format_value[(DXGI_FORMAT_R8G8B8A8_UNORM == 28) ? 1 : -1];
typedef char direct_usage_value[(DXGI_USAGE_RENDER_TARGET_OUTPUT == 0x20) ? 1 : -1];
typedef char direct_effect_value[(DXGI_SWAP_EFFECT_DISCARD == 0) ? 1 : -1];

void NativeFfiDirectAcquisitionSdkSignatures(const ID3D11DeviceVtbl* device,
    const ID3D11DeviceContextVtbl* context, const IDXGISwapChainVtbl* swap)
{
    HRESULT (WINAPI *create)(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT,
        const D3D_FEATURE_LEVEL*, UINT, UINT, const DXGI_SWAP_CHAIN_DESC*,
        IDXGISwapChain**, ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**) =
        D3D11CreateDeviceAndSwapChain;
    ULONG (STDMETHODCALLTYPE *releaseDevice)(ID3D11Device*) = device->Release;
    ULONG (STDMETHODCALLTYPE *releaseContext)(ID3D11DeviceContext*) = context->Release;
    ULONG (STDMETHODCALLTYPE *releaseSwap)(IDXGISwapChain*) = swap->Release;
    (void)create; (void)releaseDevice; (void)releaseContext; (void)releaseSwap;
}
