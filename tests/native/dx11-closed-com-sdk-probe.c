#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <windows.h>
#include <dxgi.h>
#include <d3d11.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This probe requires the Windows x64 SDK ABI.
#endif

#define ASSERT_SIZE(name, type, expected) \
    typedef char name[(sizeof(type) == (expected)) ? 1 : -1]
#define ASSERT_ALIGNMENT(name, type, expected) \
    typedef char name[(__alignof(type) == (expected)) ? 1 : -1]
#define ASSERT_OFFSET(name, type, member, expected) \
    typedef char name[(offsetof(type, member) == (expected)) ? 1 : -1]

ASSERT_SIZE(guid_size, GUID, 16);
ASSERT_ALIGNMENT(guid_alignment, GUID, 4);
ASSERT_OFFSET(guid_data1, GUID, Data1, 0);
ASSERT_OFFSET(guid_data2, GUID, Data2, 4);
ASSERT_OFFSET(guid_data3, GUID, Data3, 6);
ASSERT_OFFSET(guid_data4, GUID, Data4, 8);
ASSERT_SIZE(hresult_width, HRESULT, 4);
ASSERT_SIZE(ulong_width, ULONG, 4);
ASSERT_SIZE(interface_pointer_width, IUnknown*, 8);
ASSERT_SIZE(output_cell_width, ID3D11Texture2D**, 8);
ASSERT_SIZE(context_pointer_width, ID3D11DeviceContext*, 8);
ASSERT_OFFSET(iunknown_release_slot, IUnknownVtbl, Release, 16);
ASSERT_OFFSET(dxgi_swapchain_present_slot, IDXGISwapChainVtbl, Present, 64);
ASSERT_OFFSET(dxgi_swapchain_getbuffer_slot, IDXGISwapChainVtbl, GetBuffer, 72);
ASSERT_OFFSET(dxgi_swapchain_release_slot, IDXGISwapChainVtbl, Release, 16);
ASSERT_OFFSET(d3d11_device_creatertv_slot, ID3D11DeviceVtbl, CreateRenderTargetView, 72);
ASSERT_OFFSET(d3d11_context_clear_rtv_slot, ID3D11DeviceContextVtbl, ClearRenderTargetView, 400);
ASSERT_OFFSET(d3d11_texture2d_release_slot, ID3D11Texture2DVtbl, Release, 16);
ASSERT_OFFSET(d3d11_rtv_release_slot, ID3D11RenderTargetViewVtbl, Release, 16);
typedef char hresult_is_signed[((HRESULT)-1 < 0) ? 1 : -1];

static int CheckSdkIidBytes(void)
{
    static const BYTE unknownBytes[16] =
        { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 };
    static const BYTE dxgiObjectBytes[16] =
        { 0xB8, 0x2F, 0xC2, 0xAE, 0xF3, 0x76, 0x39, 0x46, 0x9B, 0xE0, 0x28, 0xEB, 0x43, 0xA6, 0x7A, 0x2E };
    static const BYTE dxgiDeviceSubObjectBytes[16] =
        { 0x79, 0x03, 0x3E, 0x3D, 0xDE, 0xF9, 0x58, 0x4D, 0xBB, 0x6C, 0x18, 0xD6, 0x29, 0x92, 0xF1, 0xA6 };
    static const BYTE swapChainBytes[16] =
        { 0xA0, 0x36, 0x0D, 0x31, 0xE7, 0xD2, 0x0A, 0x4C, 0xAA, 0x04, 0x6A, 0x9D, 0x23, 0xB8, 0x88, 0x6A };
    static const BYTE deviceBytes[16] =
        { 0xDB, 0x6D, 0x6F, 0xDB, 0x77, 0xAC, 0x88, 0x4E, 0x82, 0x53, 0x81, 0x9D, 0xF9, 0xBB, 0xF1, 0x40 };
    static const BYTE deviceChildBytes[16] =
        { 0xC8, 0xE5, 0x41, 0x18, 0xB0, 0x16, 0x9B, 0x48, 0xBC, 0xC8, 0x44, 0xCF, 0xB0, 0xD5, 0xDE, 0xAE };
    static const BYTE resourceBytes[16] =
        { 0xF3, 0x63, 0x8E, 0xDC, 0x2B, 0xD1, 0x52, 0x49, 0xB4, 0x7B, 0x5E, 0x45, 0x02, 0x6A, 0x86, 0x2D };
    static const BYTE texture2DBytes[16] =
        { 0xF2, 0xAA, 0x15, 0x6F, 0x08, 0xD2, 0x89, 0x4E, 0x9A, 0xB4, 0x48, 0x95, 0x35, 0xD3, 0x4F, 0x9C };
    static const BYTE viewBytes[16] =
        { 0x16, 0x12, 0x9D, 0x83, 0x2E, 0xBB, 0x2B, 0x41, 0xB7, 0xF4, 0xA9, 0xDB, 0xEB, 0xE0, 0x8E, 0xD1 };
    static const BYTE renderTargetViewBytes[16] =
        { 0x67, 0xA0, 0xDB, 0xDF, 0x8D, 0x0B, 0x65, 0x48, 0x87, 0x5B, 0xD7, 0xB4, 0x51, 0x6C, 0xC1, 0x64 };
    static const BYTE deviceContextBytes[16] =
        { 0x6C, 0xA9, 0xBF, 0xC0, 0x89, 0xE0, 0xFB, 0x44, 0x8E, 0xAF, 0x26, 0xF8, 0x79, 0x61, 0x90, 0xDA };
    return memcmp(&IID_IUnknown, unknownBytes, 16) == 0
        && memcmp(&IID_IDXGIObject, dxgiObjectBytes, 16) == 0
        && memcmp(&IID_IDXGIDeviceSubObject, dxgiDeviceSubObjectBytes, 16) == 0
        && memcmp(&IID_IDXGISwapChain, swapChainBytes, 16) == 0
        && memcmp(&IID_ID3D11Device, deviceBytes, 16) == 0
        && memcmp(&IID_ID3D11DeviceChild, deviceChildBytes, 16) == 0
        && memcmp(&IID_ID3D11Resource, resourceBytes, 16) == 0
        && memcmp(&IID_ID3D11Texture2D, texture2DBytes, 16) == 0
        && memcmp(&IID_ID3D11View, viewBytes, 16) == 0
        && memcmp(&IID_ID3D11RenderTargetView, renderTargetViewBytes, 16) == 0
        && memcmp(&IID_ID3D11DeviceContext, deviceContextBytes, 16) == 0;
}

static void CheckSdkSignatures(const IDXGISwapChainVtbl* swapChain,
    const ID3D11DeviceVtbl* device, const ID3D11Texture2DVtbl* texture,
    const ID3D11RenderTargetViewVtbl* renderTargetView, const ID3D11DeviceContextVtbl* context)
{
    HRESULT (STDMETHODCALLTYPE *present)(IDXGISwapChain*, UINT, UINT) =
        swapChain->Present;
    HRESULT (STDMETHODCALLTYPE *getBuffer)(IDXGISwapChain*, UINT, REFIID, void**) =
        swapChain->GetBuffer;
    HRESULT (STDMETHODCALLTYPE *createRenderTargetView)(ID3D11Device*, ID3D11Resource*,
        const D3D11_RENDER_TARGET_VIEW_DESC*, ID3D11RenderTargetView**) =
        device->CreateRenderTargetView;
    ULONG (STDMETHODCALLTYPE *releaseTexture)(ID3D11Texture2D*) = texture->Release;
    ULONG (STDMETHODCALLTYPE *releaseRenderTargetView)(ID3D11RenderTargetView*) =
        renderTargetView->Release;
    void (STDMETHODCALLTYPE *clearRenderTargetView)(ID3D11DeviceContext*,
        ID3D11RenderTargetView*, const FLOAT*) = context->ClearRenderTargetView;
    (void)present;
    (void)getBuffer;
    (void)createRenderTargetView;
    (void)releaseTexture;
    (void)releaseRenderTargetView;
    (void)clearRenderTargetView;
}

int main(void)
{
    (void)CheckSdkSignatures;
    return CheckSdkIidBytes() ? 0 : 1;
}
