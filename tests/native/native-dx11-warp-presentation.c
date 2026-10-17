#include <windows.h>
#include <initguid.h>
#include <dwmapi.h>
#ifndef WARP_OUTPUT_UTILITIES_INCLUDED
#define WARP_OUTPUT_WIDTH 320
#define WARP_OUTPUT_HEIGHT 320
#define WARP_OUTPUT_UTILITIES_ONLY
#include "native-dx11-warp-output.c"
#undef WARP_OUTPUT_UTILITIES_ONLY
#endif

typedef struct warp_present_window {
    DWORD thread;
    HWND handle;
    BOOL cancelled;
    BOOL minimized;
    BOOL resized;
    BOOL fixedSize;
    BOOL destroying;
    BOOL destroyed;
    HRESULT paintResult;
    ULONGLONG deadline;
} warp_present_window;

static LRESULT CALLBACK warp_present_wndproc(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
{
    warp_present_window* state = (warp_present_window*)GetWindowLongPtrW(window, GWLP_USERDATA);
    if (message == WM_NCCREATE)
    {
        const CREATESTRUCTW* creation = (const CREATESTRUCTW*)lparam;
        state = (warp_present_window*)creation->lpCreateParams;
        SetLastError(ERROR_SUCCESS);
        if (SetWindowLongPtrW(window, GWLP_USERDATA, (LONG_PTR)state) == 0
            && GetLastError() != ERROR_SUCCESS)
        {
            return FALSE;
        }
        state->handle = window;
    }
    if (state != NULL)
    {
        if (message == WM_CLOSE)
        {
            /* Teardown, not the callback, owns DestroyWindow after COM release. */
            state->cancelled = TRUE;
            return 0;
        }
        if (message == WM_SIZE)
        {
            if (wparam == SIZE_MINIMIZED)
            {
                state->minimized = TRUE;
            }
            if (state->fixedSize && (LOWORD(lparam) != WARP_OUTPUT_WIDTH
                || HIWORD(lparam) != WARP_OUTPUT_HEIGHT))
            {
                state->resized = TRUE;
            }
        }
        if (message == WM_PAINT)
        {
            PAINTSTRUCT paint = { 0 };
            SetLastError(ERROR_SUCCESS);
            HDC dc = BeginPaint(window, &paint);
            if (dc == NULL)
            {
                state->paintResult = warp_output_win32_failure("BeginPaint", GetLastError());
            }
            else if (!EndPaint(window, &paint))
            {
                state->paintResult = warp_output_win32_failure("EndPaint", GetLastError());
            }
            return 0;
        }
        if (message == WM_DESTROY)
        {
            if (!state->destroying)
            {
                state->cancelled = TRUE;
            }
            PostQuitMessage(0);
            return 0;
        }
        if (message == WM_NCDESTROY)
        {
            LRESULT result = DefWindowProcW(window, message, wparam, lparam);
            state->destroyed = TRUE;
            SetWindowLongPtrW(window, GWLP_USERDATA, 0);
            return result;
        }
    }
    return DefWindowProcW(window, message, wparam, lparam);
}

static HRESULT warp_present_exact_status(const char* stage, HRESULT result)
{
    if (result == DXGI_STATUS_OCCLUDED)
    {
        warp_output_failure("DXGI_STATUS_OCCLUDED", result);
        return E_FAIL;
    }
    if (result != S_OK)
    {
        warp_output_failure(stage, result);
        return FAILED(result) ? result : E_FAIL;
    }
    return S_OK;
}

static HRESULT warp_present_desktop(void)
{
    USEROBJECTFLAGS flags;
    WCHAR inputName[256];
    WCHAR threadName[256];
    DWORD required;
    HDESK input = OpenInputDesktop(0, FALSE, DESKTOP_READOBJECTS);
    HRESULT result = S_OK;
    if (input == NULL)
    {
        return warp_output_win32_failure("OpenInputDesktop", GetLastError());
    }
    if (!GetUserObjectInformationW(GetProcessWindowStation(), UOI_FLAGS, &flags,
            sizeof(flags), &required)
        || !GetUserObjectInformationW(input, UOI_NAME, inputName, sizeof(inputName), &required)
        || !GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), UOI_NAME,
            threadName, sizeof(threadName), &required))
    {
        result = warp_output_win32_failure("interactive desktop information", GetLastError());
    }
    else if (!(flags.dwFlags & WSF_VISIBLE) || wcscmp(inputName, threadName) != 0)
    {
        result = warp_output_failure("unavailable interactive desktop", E_ACCESSDENIED);
    }
    if (!CloseDesktop(input))
    {
        HRESULT closeResult = warp_output_win32_failure("CloseDesktop", GetLastError());
        if (SUCCEEDED(result))
        {
            result = closeResult;
        }
    }
    return result;
}

static HRESULT warp_present_pump(warp_present_window* state)
{
    MSG message;
    RECT client;
    HRESULT result;
    while (PeekMessageW(&message, NULL, 0, 0, PM_REMOVE))
    {
        if (message.message == WM_QUIT)
        {
            state->cancelled = TRUE;
        }
        else
        {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        if (state->cancelled || state->minimized || state->resized || FAILED(state->paintResult))
        {
            break;
        }
        if (GetTickCount64() >= state->deadline)
        {
            return warp_output_win32_failure("message-pump timeout", ERROR_TIMEOUT);
        }
    }
    if (GetTickCount64() >= state->deadline)
    {
        return warp_output_win32_failure("presentation timeout", ERROR_TIMEOUT);
    }
    if (state->cancelled || state->destroyed || state->minimized || state->resized
        || !IsWindowVisible(state->handle) || IsIconic(state->handle))
    {
        return warp_output_win32_failure("closed/hidden/minimized/resized window", ERROR_CANCELLED);
    }
    if (FAILED(state->paintResult))
    {
        return state->paintResult;
    }
    if (!GetClientRect(state->handle, &client))
    {
        return warp_output_win32_failure("GetClientRect", GetLastError());
    }
    if (client.right - client.left != WARP_OUTPUT_WIDTH
        || client.bottom - client.top != WARP_OUTPUT_HEIGHT)
    {
        return warp_output_win32_failure("changed/empty client size", ERROR_CANCELLED);
    }
    result = warp_present_desktop();
    return result;
}

__declspec(dllexport) HRESULT WINAPI NativeFfiPresentTriangleWarp(void)
{
    static const WCHAR className[] = L"Terrum.Provisional.WarpPresentation";
    static const D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_0 };
    static const D3D11_INPUT_ELEMENT_DESC inputElement = {
        "POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0
    };
    static const float vertices[9] = {
        0.0f, 0.6f, 0.5f, 0.6f, -0.5f, 0.5f, -0.6f, -0.5f, 0.5f
    };
    static const FLOAT clearColor[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    const DWORD style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU;
    /* Module-lifetime state cannot dangle even if DestroyWindow fails. */
    static warp_present_window window;
    static LONG active;
    WNDCLASSEXW windowClass = { 0 };
    RECT bounds = { 0, 0, WARP_OUTPUT_WIDTH, WARP_OUTPUT_HEIGHT };
    RECT client;
    ATOM registered = 0;
    HWND handle = NULL;
    HINSTANCE instance = GetModuleHandleW(NULL);
    BOOL composition = FALSE;
    WCHAR shaderPath[WARP_OUTPUT_PATH_CAPACITY];
    HMODULE compilerModule = NULL;
    HMODULE d3d11Module;
    FARPROC symbol;
    warp_output_compiler compiler;
    warp_output_shader vertexCode = { 0 };
    warp_output_shader pixelCode = { 0 };
    ID3D11Device* device = NULL;
    ID3D11DeviceContext* context = NULL;
    IDXGISwapChain* swapChain = NULL;
    IDXGIFactory* factory = NULL;
    ID3D11VertexShader* vertexShader = NULL;
    ID3D11PixelShader* pixelShader = NULL;
    ID3D11InputLayout* inputLayout = NULL;
    ID3D11Buffer* vertexBuffer = NULL;
    ID3D11Texture2D* target = NULL;
    ID3D11RenderTargetView* targetView = NULL;
    ID3D11Texture2D* staging = NULL;
    D3D_FEATURE_LEVEL selectedFeatureLevel = 0;
    DXGI_SWAP_CHAIN_DESC swapDescription = { 0 };
    D3D11_BUFFER_DESC bufferDescription = {
        36, D3D11_USAGE_IMMUTABLE, D3D11_BIND_VERTEX_BUFFER, 0, 0, 0
    };
    D3D11_SUBRESOURCE_DATA initialData = { vertices, 0, 0 };
    D3D11_TEXTURE2D_DESC textureDescription = { 0 };
    D3D11_VIEWPORT viewport = {
        0.0f, 0.0f, (FLOAT)WARP_OUTPUT_WIDTH, (FLOAT)WARP_OUTPUT_HEIGHT, 0.0f, 1.0f
    };
    D3D11_MAPPED_SUBRESOURCE data = { 0 };
    BOOL mapped = FALSE;
    UINT stride = 12;
    UINT offset = 0;
    HRESULT result;
    if (InterlockedCompareExchange(&active, 1, 0) != 0)
    {
        return warp_output_win32_failure("concurrent presentation rejected", ERROR_BUSY);
    }
    if (window.handle != NULL && !window.destroyed)
    {
        InterlockedExchange(&active, 0);
        return warp_output_failure("previous window destruction failed", E_UNEXPECTED);
    }
    memset(&window, 0, sizeof(window));
    window.thread = GetCurrentThreadId();
    window.deadline = GetTickCount64() + 15000;
    result = warp_present_desktop();
    if (FAILED(result))
    {
        goto cleanup;
    }
    result = warp_present_exact_status("DwmIsCompositionEnabled", DwmIsCompositionEnabled(&composition));
    if (FAILED(result) || !composition)
    {
        if (SUCCEEDED(result))
        {
            result = warp_output_failure("composition unavailable", E_FAIL);
        }
        goto cleanup;
    }
    windowClass.cbSize = sizeof(windowClass);
    windowClass.lpfnWndProc = warp_present_wndproc;
    windowClass.hInstance = instance;
    windowClass.lpszClassName = className;
    registered = RegisterClassExW(&windowClass);
    if (registered == 0)
    {
        result = warp_output_win32_failure("RegisterClassExW", GetLastError());
        goto cleanup;
    }
    if (!AdjustWindowRectEx(&bounds, style, FALSE, 0))
    {
        result = warp_output_win32_failure("AdjustWindowRectEx", GetLastError());
        goto cleanup;
    }
    handle = CreateWindowExW(0, className, L"Terrum WARP (test only)", style,
        CW_USEDEFAULT, CW_USEDEFAULT, bounds.right - bounds.left, bounds.bottom - bounds.top,
        NULL, NULL, instance, &window);
    if (handle == NULL)
    {
        result = warp_output_win32_failure("CreateWindowExW", GetLastError());
        goto cleanup;
    }
    if (!GetClientRect(handle, &client))
    {
        result = warp_output_win32_failure("initial client size", GetLastError());
        goto cleanup;
    }
    if (client.right <= client.left || client.bottom <= client.top)
    {
        result = warp_output_failure("empty initial client size", E_UNEXPECTED);
        goto cleanup;
    }
    window.fixedSize = TRUE;
    ShowWindow(handle, SW_SHOWNORMAL);
    result = warp_present_pump(&window);
    if (FAILED(result))
    {
        goto cleanup;
    }
    result = warp_output_shader_path(shaderPath);
    if (FAILED(result))
    {
        goto cleanup;
    }
    d3d11Module = GetModuleHandleW(L"d3d11.dll");
    if (d3d11Module == NULL)
    {
        result = warp_output_win32_failure("D3D11 startup import", GetLastError());
        goto cleanup;
    }
    result = warp_output_system_module(d3d11Module, L"d3d11.dll");
    if (FAILED(result))
    {
        goto cleanup;
    }
    compilerModule = LoadLibraryExW(L"D3DCompiler_47.dll", NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (compilerModule == NULL)
    {
        result = warp_output_win32_failure("LoadLibraryExW compiler", GetLastError());
        goto cleanup;
    }
    result = warp_output_system_module(compilerModule, L"D3DCompiler_47.dll");
    if (FAILED(result))
    {
        goto cleanup;
    }
    symbol = GetProcAddress(compilerModule, "D3DCompileFromFile");
    if (symbol == NULL)
    {
        result = warp_output_win32_failure("D3DCompileFromFile export", GetLastError());
        goto cleanup;
    }
    {
        typedef char compiler_pointer_width[(sizeof(compiler) == sizeof(symbol)) ? 1 : -1];
        (void)sizeof(compiler_pointer_width);
    }
    memcpy(&compiler, &symbol, sizeof(compiler));
    result = warp_output_compile(compiler, shaderPath, "VertexMain", "vs_5_0", &vertexCode);
    if (FAILED(result))
    {
        goto cleanup;
    }
    result = warp_output_compile(compiler, shaderPath, "PixelMain", "ps_5_0", &pixelCode);
    if (FAILED(result))
    {
        goto cleanup;
    }
    swapDescription.BufferDesc.Width = (UINT)(client.right - client.left);
    swapDescription.BufferDesc.Height = (UINT)(client.bottom - client.top);
    swapDescription.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    swapDescription.SampleDesc.Count = 1;
    swapDescription.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    swapDescription.BufferCount = 1;
    swapDescription.OutputWindow = handle;
    swapDescription.Windowed = TRUE;
    swapDescription.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
    result = D3D11CreateDeviceAndSwapChain(NULL, D3D_DRIVER_TYPE_WARP, NULL, 0, featureLevels,
        1, D3D11_SDK_VERSION, &swapDescription, &swapChain, &device, &selectedFeatureLevel, &context);
    if (FAILED(result))
    {
        warp_output_failure("D3D11CreateDeviceAndSwapChain WARP", result);
        goto cleanup;
    }
    if (device == NULL || context == NULL || swapChain == NULL
        || selectedFeatureLevel != D3D_FEATURE_LEVEL_11_0)
    {
        result = warp_output_failure("WARP success without required outputs/feature level", E_UNEXPECTED);
        goto cleanup;
    }
    result = swapChain->lpVtbl->GetParent(swapChain, &IID_IDXGIFactory, (void**)&factory);
    if (FAILED(result) || factory == NULL)
    {
        result = warp_output_failure("GetParent factory", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    result = factory->lpVtbl->MakeWindowAssociation(factory, handle, DXGI_MWA_NO_ALT_ENTER);
    if (FAILED(result))
    {
        warp_output_failure("MakeWindowAssociation", result);
        goto cleanup;
    }
    result = device->lpVtbl->CreateVertexShader(device, vertexCode.bytes, vertexCode.size,
        NULL, &vertexShader);
    if (FAILED(result) || vertexShader == NULL)
    {
        result = warp_output_failure("CreateVertexShader", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateInputLayout(device, &inputElement, 1, vertexCode.bytes,
        vertexCode.size, &inputLayout);
    if (FAILED(result) || inputLayout == NULL)
    {
        result = warp_output_failure("CreateInputLayout", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateBuffer(device, &bufferDescription, &initialData, &vertexBuffer);
    if (FAILED(result) || vertexBuffer == NULL)
    {
        result = warp_output_failure("CreateBuffer", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreatePixelShader(device, pixelCode.bytes, pixelCode.size,
        NULL, &pixelShader);
    if (FAILED(result) || pixelShader == NULL)
    {
        result = warp_output_failure("CreatePixelShader", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    result = swapChain->lpVtbl->GetBuffer(swapChain, 0, &IID_ID3D11Texture2D, (void**)&target);
    if (FAILED(result) || target == NULL)
    {
        result = warp_output_failure("GetBuffer zero", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    target->lpVtbl->GetDesc(target, &textureDescription);
    if (textureDescription.Width != swapDescription.BufferDesc.Width
        || textureDescription.Height != swapDescription.BufferDesc.Height
        || textureDescription.MipLevels != 1 || textureDescription.ArraySize != 1
        || textureDescription.Format != DXGI_FORMAT_R8G8B8A8_UNORM
        || textureDescription.SampleDesc.Count != 1 || textureDescription.SampleDesc.Quality != 0)
    {
        result = warp_output_failure("back-buffer description", E_UNEXPECTED);
        goto cleanup;
    }
    result = device->lpVtbl->CreateRenderTargetView(device, (ID3D11Resource*)target,
        NULL, &targetView);
    if (FAILED(result) || targetView == NULL)
    {
        result = warp_output_failure("CreateRenderTargetView", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    textureDescription.Usage = D3D11_USAGE_STAGING;
    textureDescription.BindFlags = 0;
    textureDescription.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    textureDescription.MiscFlags = 0;
    result = device->lpVtbl->CreateTexture2D(device, &textureDescription, NULL, &staging);
    if (FAILED(result) || staging == NULL)
    {
        result = warp_output_failure("CreateTexture2D staging", FAILED(result) ? result : E_UNEXPECTED);
        goto cleanup;
    }
    context->lpVtbl->ClearRenderTargetView(context, targetView, clearColor);
    context->lpVtbl->OMSetRenderTargets(context, 1, &targetView, NULL);
    context->lpVtbl->PSSetShader(context, pixelShader, NULL, 0);
    context->lpVtbl->VSSetShader(context, vertexShader, NULL, 0);
    context->lpVtbl->IASetInputLayout(context, inputLayout);
    context->lpVtbl->IASetVertexBuffers(context, 0, 1, &vertexBuffer, &stride, &offset);
    context->lpVtbl->IASetPrimitiveTopology(context, D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->lpVtbl->RSSetViewports(context, 1, &viewport);
    context->lpVtbl->Draw(context, 3, 0);
    context->lpVtbl->CopyResource(context, (ID3D11Resource*)staging, (ID3D11Resource*)target);
    result = context->lpVtbl->Map(context, (ID3D11Resource*)staging, 0, D3D11_MAP_READ, 0, &data);
    if (FAILED(result))
    {
        warp_output_failure("Map staging", result);
        goto cleanup;
    }
    mapped = TRUE;
    result = warp_output_check_pixels(&data);
    context->lpVtbl->Unmap(context, (ID3D11Resource*)staging, 0);
    mapped = FALSE;
    if (FAILED(result))
    {
        goto cleanup;
    }
    result = warp_present_pump(&window);
    if (FAILED(result))
    {
        goto cleanup;
    }
    result = warp_present_exact_status("Present(1,0)", swapChain->lpVtbl->Present(swapChain, 1, 0));
    if (FAILED(result))
    {
        goto cleanup;
    }
    result = warp_present_pump(&window);
    if (FAILED(result))
    {
        goto cleanup;
    }
    result = warp_present_exact_status("DwmFlush", DwmFlush());
    if (FAILED(result))
    {
        goto cleanup;
    }
    result = warp_present_pump(&window);

cleanup:
    if (device != NULL && (result == DXGI_ERROR_DEVICE_REMOVED || result == DXGI_ERROR_DEVICE_RESET))
    {
        HRESULT reason = device->lpVtbl->GetDeviceRemovedReason(device);
        warp_output_failure("GetDeviceRemovedReason", reason);
        if (FAILED(reason))
        {
            result = reason;
        }
    }
    if (mapped)
    {
        context->lpVtbl->Unmap(context, (ID3D11Resource*)staging, 0);
    }
    if (context != NULL)
    {
        context->lpVtbl->ClearState(context);
        context->lpVtbl->Release(context);
    }
    if (staging != NULL)
    {
        staging->lpVtbl->Release(staging);
    }
    if (targetView != NULL)
    {
        targetView->lpVtbl->Release(targetView);
    }
    if (target != NULL)
    {
        target->lpVtbl->Release(target);
    }
    if (pixelShader != NULL)
    {
        pixelShader->lpVtbl->Release(pixelShader);
    }
    if (vertexBuffer != NULL)
    {
        vertexBuffer->lpVtbl->Release(vertexBuffer);
    }
    if (inputLayout != NULL)
    {
        inputLayout->lpVtbl->Release(inputLayout);
    }
    if (vertexShader != NULL)
    {
        vertexShader->lpVtbl->Release(vertexShader);
    }
    if (factory != NULL)
    {
        factory->lpVtbl->Release(factory);
    }
    if (swapChain != NULL)
    {
        swapChain->lpVtbl->Release(swapChain);
    }
    if (device != NULL)
    {
        device->lpVtbl->Release(device);
    }
    warp_output_release_shader(&pixelCode);
    warp_output_release_shader(&vertexCode);
    if (compilerModule != NULL && !FreeLibrary(compilerModule))
    {
        HRESULT unloadResult = warp_output_win32_failure("FreeLibrary compiler", GetLastError());
        if (SUCCEEDED(result))
        {
            result = unloadResult;
        }
    }
    if (handle != NULL)
    {
        window.destroying = TRUE;
        if (GetCurrentThreadId() != window.thread || !DestroyWindow(handle))
        {
            HRESULT destroyResult = warp_output_win32_failure("DestroyWindow/creating thread", GetLastError());
            if (SUCCEEDED(result))
            {
                result = destroyResult;
            }
        }
    }
    if (registered != 0 && !UnregisterClassW(className, instance))
    {
        HRESULT unregisterResult = warp_output_win32_failure("UnregisterClassW", GetLastError());
        if (SUCCEEDED(result))
        {
            result = unregisterResult;
        }
    }
    InterlockedExchange(&active, 0);
    return result;
}
