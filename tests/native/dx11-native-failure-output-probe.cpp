#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <dxgi.h>
#include <stdio.h>

static int expect_failure_null(const char* operation, HRESULT result, const void* output)
{
    if (SUCCEEDED(result) || output != nullptr)
    {
        fprintf(stderr, "%s: HRESULT=0x%08lX output=%p\n", operation,
            static_cast<unsigned long>(result), output);
        return 1;
    }

    printf("%s: HRESULT=0x%08lX output=null\n", operation,
        static_cast<unsigned long>(result));
    return 0;
}

int wmain()
{
    ID3DBlob* code = nullptr;
    ID3DBlob* diagnostics = nullptr;
    HRESULT result = D3DCompileFromFile(L"triangle.hlsl", nullptr, nullptr,
        "MissingEntry", "vs_5_0", 0, 0, &code, &diagnostics);
    if (expect_failure_null("D3DCompileFromFile-code", result, code) != 0)
    {
        return 1;
    }
    printf("D3DCompileFromFile-diagnostics: output=%s\n",
        diagnostics == nullptr ? "null" : "non-null");
    if (diagnostics != nullptr)
    {
        diagnostics->Release();
    }

    WNDCLASSW windowClass = {};
    windowClass.lpfnWndProc = DefWindowProcW;
    windowClass.hInstance = GetModuleHandleW(nullptr);
    windowClass.lpszClassName = L"TerrumNativeFailureOutputProbe";
    if (RegisterClassW(&windowClass) == 0)
    {
        fprintf(stderr, "RegisterClassW failed: %lu\n", GetLastError());
        return 1;
    }

    HWND window = CreateWindowExW(0, windowClass.lpszClassName, L"",
        WS_OVERLAPPEDWINDOW, 0, 0, 32, 32, nullptr, nullptr,
        windowClass.hInstance, nullptr);
    if (window == nullptr)
    {
        fprintf(stderr, "CreateWindowExW failed: %lu\n", GetLastError());
        return 1;
    }

    const D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_0 };
    DXGI_SWAP_CHAIN_DESC description = {};
    description.BufferDesc.Width = 32;
    description.BufferDesc.Height = 32;
    description.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    description.SampleDesc.Count = 1;
    description.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    description.BufferCount = 1;
    description.Windowed = TRUE;
    description.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

    IDXGISwapChain* failedSwapChain = nullptr;
    ID3D11Device* failedDevice = nullptr;
    ID3D11DeviceContext* failedContext = nullptr;
    D3D_FEATURE_LEVEL failedFeatureLevel = static_cast<D3D_FEATURE_LEVEL>(0);
    result = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0,
        featureLevels, 1, D3D11_SDK_VERSION, &description, &failedSwapChain,
        &failedDevice, &failedFeatureLevel, &failedContext);
    if (FAILED(result) && failedSwapChain == nullptr && failedDevice == nullptr
        && failedContext == nullptr)
    {
        printf("D3D11CreateDeviceAndSwapChain-invalid-window: HRESULT=0x%08lX "
            "swapChain=null device=null context=null\n", static_cast<unsigned long>(result));
    }
    else
    {
        fprintf(stderr, "D3D11CreateDeviceAndSwapChain-invalid-window: "
            "HRESULT=0x%08lX swapChain=%p device=%p context=%p\n",
            static_cast<unsigned long>(result), failedSwapChain, failedDevice, failedContext);
        return 1;
    }

    IDXGISwapChain* swapChain = nullptr;
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    D3D_FEATURE_LEVEL selectedFeatureLevel = static_cast<D3D_FEATURE_LEVEL>(0);
    description.OutputWindow = window;
    result = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0,
        featureLevels, 1, D3D11_SDK_VERSION, &description, &swapChain,
        &device, &selectedFeatureLevel, &context);
    if (FAILED(result) || swapChain == nullptr || device == nullptr || context == nullptr)
    {
        fprintf(stderr, "D3D11CreateDeviceAndSwapChain-success setup failed: "
            "HRESULT=0x%08lX swapChain=%p device=%p context=%p\n",
            static_cast<unsigned long>(result), swapChain, device, context);
        return 1;
    }

    ID3D11Texture2D* backBuffer = nullptr;
    result = swapChain->GetBuffer(static_cast<UINT>(-1), __uuidof(ID3D11Texture2D),
        reinterpret_cast<void**>(&backBuffer));
    if (expect_failure_null("IDXGISwapChain::GetBuffer-invalid-index", result, backBuffer) != 0)
    {
        return 1;
    }

    const unsigned char invalidBytecode[] = { 0, 0, 0, 0 };
    ID3D11VertexShader* vertexShader = nullptr;
    result = device->CreateVertexShader(invalidBytecode, sizeof(invalidBytecode),
        nullptr, &vertexShader);
    if (expect_failure_null("ID3D11Device::CreateVertexShader-invalid-bytecode",
        result, vertexShader) != 0)
    {
        return 1;
    }

    ID3D11PixelShader* pixelShader = nullptr;
    result = device->CreatePixelShader(invalidBytecode, sizeof(invalidBytecode),
        nullptr, &pixelShader);
    if (expect_failure_null("ID3D11Device::CreatePixelShader-invalid-bytecode",
        result, pixelShader) != 0)
    {
        return 1;
    }

    ID3D11RenderTargetView* renderTargetView = nullptr;
    result = device->CreateRenderTargetView(nullptr, nullptr, &renderTargetView);
    if (expect_failure_null("ID3D11Device::CreateRenderTargetView-null-resource",
        result, renderTargetView) != 0)
    {
        return 1;
    }

    context->Release();
    swapChain->Release();
    device->Release();
    DestroyWindow(window);
    UnregisterClassW(windowClass.lpszClassName, windowClass.hInstance);
    return 0;
}
