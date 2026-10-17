#include <windows.h>
#include <d3d11.h>
#include <stdio.h>

#pragma comment(linker, "/export:D3D11CreateDeviceAndSwapChain=NativeFfiShadowD3D11")

HRESULT WINAPI NativeFfiShadowD3D11(
    IDXGIAdapter* adapter, D3D_DRIVER_TYPE driver, HMODULE software, UINT flags,
    const D3D_FEATURE_LEVEL* levels, UINT count, UINT sdk, const DXGI_SWAP_CHAIN_DESC* description,
    IDXGISwapChain** swap, ID3D11Device** device, D3D_FEATURE_LEVEL* selected, ID3D11DeviceContext** context)
{
    (void)adapter; (void)driver; (void)software; (void)flags;
    (void)levels; (void)count; (void)sdk; (void)description; (void)selected;
    *swap = NULL; *device = NULL; *context = NULL;
    fputs("APP_LOCAL_D3D11_EXECUTED\n", stderr);
    return E_ACCESSDENIED;
}
