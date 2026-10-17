#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This test-only DX11 pixel probe requires Windows x64.
#endif

#define CHECK(condition) do { if (!(condition)) { \
    fprintf(stderr, "sample pixel probe line %d: %s\n", __LINE__, #condition); ExitProcess(97); } } while (0)

static IDXGISwapChain* s_swap;
static IDXGISwapChainVtbl* s_swapOriginal;
static IDXGISwapChainVtbl s_swapTable;
static ID3D11Device* s_device;
static ID3D11DeviceContext* s_context;
static D3D_DRIVER_TYPE s_requestedDriver;
static HWND s_window;
static UINT s_initialWidth;
static UINT s_initialHeight;
static BOOL s_initialPixelsVerified;
static BOOL s_resizedPixelsVerified;
static char s_report[MAX_PATH];

static void Report(char event)
{
    DWORD written = 0;
    HANDLE file = CreateFileA(s_report, FILE_APPEND_DATA, FILE_SHARE_READ, NULL,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    CHECK(file != INVALID_HANDLE_VALUE);
    CHECK(WriteFile(file, &event, 1, &written, NULL) && written == 1);
    CHECK(CloseHandle(file));
}

static int PixelMatches(const BYTE* actual, const int* expected)
{
    unsigned channel;
    for (channel = 0; channel < 4; ++channel)
    {
        int tolerance = channel == 3 ? 0 : 1;
        if (abs((int)actual[channel] - expected[channel]) > tolerance)
        {
            return 0;
        }
    }
    return 1;
}

static void CheckPixel(const BYTE* actual, const int* expected)
{
    CHECK(PixelMatches(actual, expected));
}

static double EdgeDistance(
    double ax, double ay, double bx, double by, double edgeLength, double px, double py)
{
    double dx = bx - ax;
    double dy = by - ay;
    return (dx * (py - ay) - dy * (px - ax)) / edgeLength;
}

static BOOL CheckTriangle(
    const BYTE* pixels,
    UINT width,
    UINT height,
    UINT rowPitch,
    const int* interior,
    const int* clear)
{
    const double ax = width * 0.5;
    const double ay = height * 0.2;
    const double bx = width * 0.8;
    const double by = height * 0.75;
    const double cx = width * 0.2;
    const double cy = height * 0.75;
    const double edgeABLength = sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
    const double edgeBCLength = sqrt((cx - bx) * (cx - bx) + (cy - by) * (cy - by));
    const double edgeCALength = sqrt((ax - cx) * (ax - cx) + (ay - cy) * (ay - cy));
    const uint64_t expectedInterior = (uint64_t)(0.5 * (bx - cx) * (by - ay));
    const uint64_t areaTolerance = (uint64_t)2 * ((uint64_t)width + height);
    uint64_t interiorCount = 0;
    BOOL matchesGeometry = TRUE;
    UINT x;
    UINT y;

    // Clip-space vertices map to these viewport points; leave a two-pixel edge band
    // for D3D11's rasterizer fill convention.
    for (y = 0; y < height; ++y)
    {
        for (x = 0; x < width; ++x)
        {
            const BYTE* pixel = pixels + (SIZE_T)y * rowPitch + (SIZE_T)x * 4;
            const double px = x + 0.5;
            const double py = y + 0.5;
            const double edgeA = EdgeDistance(ax, ay, bx, by, edgeABLength, px, py);
            const double edgeB = EdgeDistance(bx, by, cx, cy, edgeBCLength, px, py);
            const double edgeC = EdgeDistance(cx, cy, ax, ay, edgeCALength, px, py);
            const int isInterior = PixelMatches(pixel, interior);

            CHECK(isInterior || PixelMatches(pixel, clear));
            if (isInterior)
            {
                ++interiorCount;
            }
            if (edgeA >= 2.0 && edgeB >= 2.0 && edgeC >= 2.0)
            {
                if (!isInterior)
                {
                    matchesGeometry = FALSE;
                }
            }
            else if (edgeA <= -2.0 || edgeB <= -2.0 || edgeC <= -2.0)
            {
                if (isInterior)
                {
                    matchesGeometry = FALSE;
                }
            }
        }
    }

    if (interiorCount > expectedInterior
        ? interiorCount - expectedInterior > areaTolerance
        : expectedInterior - interiorCount > areaTolerance)
    {
        matchesGeometry = FALSE;
    }
    return matchesGeometry;
}

static void ReadBackBufferIfNeeded(void)
{
    static const int interior[4] = { 26, 204, 230, 255 };
    static const int clear[4] = { 20, 31, 46, 255 };
    BOOL initial = !s_initialPixelsVerified;
    D3D11_TEXTURE2D_DESC description;
    D3D11_MAPPED_SUBRESOURCE mapped;
    ID3D11Texture2D* backBuffer = NULL;
    ID3D11Texture2D* staging = NULL;
    BOOL matchesGeometry;
    HRESULT result;

    CHECK(s_swap && s_device && s_context && s_window && IsWindow(s_window));
    result = s_swapOriginal->GetBuffer(s_swap, 0, &IID_ID3D11Texture2D, (void**)&backBuffer);
    CHECK(SUCCEEDED(result) && backBuffer);
    backBuffer->lpVtbl->GetDesc(backBuffer, &description);
    CHECK(description.Width > 400 && description.Height > 300
        && description.Format == DXGI_FORMAT_R8G8B8A8_UNORM
        && description.SampleDesc.Count == 1);

    if (!initial && description.Width == s_initialWidth
        && description.Height == s_initialHeight)
    {
        backBuffer->lpVtbl->Release(backBuffer);
        return;
    }
    if (initial)
    {
        s_initialWidth = description.Width;
        s_initialHeight = description.Height;
    }
    else
    {
        CHECK(description.Width != s_initialWidth || description.Height != s_initialHeight);
    }

    description.Usage = D3D11_USAGE_STAGING;
    description.BindFlags = 0;
    description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    description.MiscFlags = 0;
    result = s_device->lpVtbl->CreateTexture2D(s_device, &description, NULL, &staging);
    CHECK(SUCCEEDED(result) && staging);
    s_context->lpVtbl->CopyResource(s_context, (ID3D11Resource*)staging, (ID3D11Resource*)backBuffer);
    result = s_context->lpVtbl->Map(s_context, (ID3D11Resource*)staging, 0, D3D11_MAP_READ, 0, &mapped);
    CHECK(SUCCEEDED(result) && mapped.pData && mapped.RowPitch >= description.Width * 4);
    CheckPixel((const BYTE*)mapped.pData, clear);
    CheckPixel((const BYTE*)mapped.pData
        + (SIZE_T)(description.Height / 2) * mapped.RowPitch
        + (SIZE_T)(description.Width / 2) * 4, interior);
    matchesGeometry = CheckTriangle((const BYTE*)mapped.pData,
        description.Width, description.Height, mapped.RowPitch, interior, clear);
    s_context->lpVtbl->Unmap(s_context, (ID3D11Resource*)staging, 0);
    staging->lpVtbl->Release(staging);
    backBuffer->lpVtbl->Release(backBuffer);
    if (initial)
    {
        s_initialPixelsVerified = TRUE;
        Report('I');
    }
    else
    {
        s_resizedPixelsVerified = TRUE;
        Report('R');
    }
    if (!matchesGeometry)
    {
        Report('G');
    }
}

static HRESULT STDMETHODCALLTYPE PresentWithPixelProbe(
    IDXGISwapChain* receiver, UINT interval, UINT flags)
{
    HRESULT result;
    CHECK(receiver == s_swap && interval == 1 && flags == 0);
    if (!s_resizedPixelsVerified)
    {
        ReadBackBufferIfNeeded();
    }
    result = s_swapOriginal->Present(receiver, interval, flags);
    CHECK(SUCCEEDED(result));
    Report('p');
    if (s_resizedPixelsVerified)
    {
        s_swap->lpVtbl = s_swapOriginal;
    }
    return result;
}

static HRESULT WINAPI CreateDeviceAndSwapChain(
    IDXGIAdapter* adapter,
    D3D_DRIVER_TYPE driver,
    HMODULE software,
    UINT flags,
    const D3D_FEATURE_LEVEL* levels,
    UINT count,
    UINT sdk,
    const DXGI_SWAP_CHAIN_DESC* description,
    IDXGISwapChain** swap,
    ID3D11Device** device,
    D3D_FEATURE_LEVEL* selected,
    ID3D11DeviceContext** context)
{
    HRESULT result;
    CHECK(driver == s_requestedDriver && !adapter && !software && !flags
        && levels && count == 1 && levels[0] == D3D_FEATURE_LEVEL_11_0
        && sdk == D3D11_SDK_VERSION && description && description->OutputWindow
        && swap && !*swap && device && !*device && selected && !*selected
        && context && !*context);
    result = D3D11CreateDeviceAndSwapChain(adapter, s_requestedDriver, software, flags,
        levels, count, sdk, description, swap, device, selected, context);
    CHECK(SUCCEEDED(result) && *swap && *device && *context);
    s_swap = *swap;
    s_device = *device;
    s_context = *context;
    s_window = description->OutputWindow;
    s_swapOriginal = s_swap->lpVtbl;
    s_swapTable = *s_swapOriginal;
    s_swapTable.Present = PresentWithPixelProbe;
    s_swap->lpVtbl = &s_swapTable;
    return result;
}

__declspec(dllexport) FARPROC WINAPI NativeFfiSamplePixelsGetProcAddress(
    HMODULE module, LPCSTR name)
{
    if (strcmp(name, "D3D11CreateDeviceAndSwapChain") == 0)
    {
        return (FARPROC)CreateDeviceAndSwapChain;
    }
    return GetProcAddress(module, name);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance;
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        char requestedDriver[16];
        DWORD length;
        CHECK(GetEnvironmentVariableA("TERRUM_DX11_SAMPLE_PIXELS_REPORT",
            s_report, sizeof(s_report)) > 0);
        length = GetEnvironmentVariableA("TERRUM_DX11_SAMPLE_PIXELS_DRIVER",
            requestedDriver, (DWORD)sizeof(requestedDriver));
        CHECK(length > 0 && length < (DWORD)sizeof(requestedDriver));
        if (strcmp(requestedDriver, "warp") == 0)
        {
            s_requestedDriver = D3D_DRIVER_TYPE_WARP;
        }
        else if (strcmp(requestedDriver, "hardware") == 0)
        {
            s_requestedDriver = D3D_DRIVER_TYPE_HARDWARE;
        }
        else
        {
            CHECK(FALSE);
        }
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        Report('d');
        CHECK(s_initialPixelsVerified && s_resizedPixelsVerified);
        Report('Z');
    }
    return TRUE;
}
