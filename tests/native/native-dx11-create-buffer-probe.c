#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <windows.h>
#include <d3d11.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This fixture requires the Windows x64 ABI.
#endif

typedef struct FixtureDevice
{
    const ID3D11DeviceVtbl* lpVtbl;
} FixtureDevice;

typedef struct FixtureBuffer
{
    const ID3D11BufferVtbl* lpVtbl;
    ULONG references;
    ULONG releases;
} FixtureBuffer;

typedef char fixture_hresult_width[(sizeof(HRESULT) == 4) ? 1 : -1];
typedef char fixture_ulong_width[(sizeof(ULONG) == 4) ? 1 : -1];
typedef char fixture_output_cell_width[(sizeof(ID3D11Buffer**) == 8) ? 1 : -1];
typedef char fixture_createbuffer_slot[(offsetof(ID3D11DeviceVtbl, CreateBuffer) == 24) ? 1 : -1];
typedef char fixture_release_slot[(offsetof(ID3D11BufferVtbl, Release) == 16) ? 1 : -1];

static ID3D11DeviceVtbl s_device_vtable;
static ID3D11BufferVtbl s_buffer_vtable;
static __declspec(thread) FixtureDevice s_device;
static __declspec(thread) FixtureBuffer s_buffer;
static __declspec(thread) UINT s_mode;
static __declspec(thread) UINT s_active;
static __declspec(thread) UINT s_createCalls;
static __declspec(thread) UINT s_error;
static __declspec(thread) UINT s_checked;
static __declspec(thread) ID3D11Buffer** s_outputCell;
static __declspec(thread) const D3D11_BUFFER_DESC* s_description;
static __declspec(thread) const D3D11_SUBRESOURCE_DATA* s_initialData;

static const float s_expectedVertices[9] = {
    0.0f, 0.6f, 0.5f,
    0.6f, -0.5f, 0.5f,
    -0.6f, -0.5f, 0.5f,
};
static const HRESULT s_failure = (HRESULT)0x80004005L;

static __declspec(noinline) uint64_t ClobberVolatile(
    uint64_t first, uint64_t second, uint64_t third, uint64_t fourth)
{
    volatile uint64_t value = first ^ second ^ third ^ fourth;
    return value;
}

static ULONG STDMETHODCALLTYPE FixtureRelease(ID3D11Buffer* buffer)
{
    if (!s_active || buffer != (ID3D11Buffer*)&s_buffer
        || s_buffer.lpVtbl != &s_buffer_vtable || s_buffer.references != 1
        || s_buffer.releases != 0 || s_checked)
    {
        __fastfail(7);
    }
    s_buffer.releases++;
    s_buffer.references--;
    if (!s_buffer.references)
    {
        s_buffer.lpVtbl = NULL;
    }
    (void)ClobberVolatile(17, 29, 41, 53);
    return s_buffer.references;
}

static int32_t CheckDescriptorBytes(const D3D11_BUFFER_DESC* description,
    const D3D11_SUBRESOURCE_DATA* initialData)
{
    const D3D11_BUFFER_DESC expectedDescription = {
        36, D3D11_USAGE_IMMUTABLE, D3D11_BIND_VERTEX_BUFFER, 0, 0, 0
    };
    const D3D11_SUBRESOURCE_DATA expectedData = {
        initialData ? initialData->pSysMem : NULL, 0, 0
    };
    return description && initialData
        && memcmp(description, &expectedDescription, sizeof(expectedDescription)) == 0
        && initialData->pSysMem && ((uintptr_t)initialData->pSysMem & 3) == 0
        && initialData->SysMemPitch == 0 && initialData->SysMemSlicePitch == 0
        && memcmp(initialData, &expectedData, sizeof(expectedData)) == 0
        && memcmp(initialData->pSysMem, s_expectedVertices, sizeof(s_expectedVertices)) == 0;
}

static HRESULT STDMETHODCALLTYPE FixtureCreateBuffer(ID3D11Device* device,
    const D3D11_BUFFER_DESC* description, const D3D11_SUBRESOURCE_DATA* initialData,
    ID3D11Buffer** output)
{
    if (!s_active || s_createCalls++)
    {
        __fastfail(7);
    }
    if (device != (ID3D11Device*)&s_device || !description || !initialData
        || !output || ((uintptr_t)output & 7) || *output
        || !CheckDescriptorBytes(description, initialData))
    {
        s_error = 1;
        return E_INVALIDARG;
    }

    s_outputCell = output;
    s_description = description;
    s_initialData = initialData;
    /* Mode 2 tests defensive cleanup only; it is not a claim about real D3D11 failure outputs. */
    if (s_mode != 1)
    {
        s_buffer.lpVtbl = &s_buffer_vtable;
        s_buffer.references = 1;
        s_buffer.releases = 0;
        *output = (ID3D11Buffer*)&s_buffer;
    }
    (void)ClobberVolatile(67, 79, 83, 97);
    return s_mode == 0 ? S_OK : s_failure;
}

__declspec(dllexport) uint64_t WINAPI NativeFfiAcquireCreateBufferDevice(UINT mode)
{
    if (mode > 2 || s_active || s_buffer.references)
    {
        __fastfail(7);
    }
    memset(&s_buffer, 0, sizeof(s_buffer));
    s_device.lpVtbl = &s_device_vtable;
    s_device_vtable.CreateBuffer = FixtureCreateBuffer;
    s_buffer_vtable.Release = FixtureRelease;
    s_mode = mode;
    s_active = 1;
    s_createCalls = 0;
    s_error = 0;
    s_checked = 0;
    s_outputCell = NULL;
    s_description = NULL;
    s_initialData = NULL;
    return (uint64_t)(uintptr_t)(ID3D11Device*)&s_device;
}

__declspec(dllexport) int32_t WINAPI NativeFfiCheckCreateBuffer(UINT mode,
    uint64_t deviceAddress, uint64_t descriptionAddress, uint64_t initialDataAddress,
    int32_t result, uint64_t outputCellAddress)
{
    ID3D11Device* device = (ID3D11Device*)(uintptr_t)deviceAddress;
    const D3D11_BUFFER_DESC* description =
        (const D3D11_BUFFER_DESC*)(uintptr_t)descriptionAddress;
    const D3D11_SUBRESOURCE_DATA* initialData =
        (const D3D11_SUBRESOURCE_DATA*)(uintptr_t)initialDataAddress;
    ID3D11Buffer** output = (ID3D11Buffer**)(uintptr_t)outputCellAddress;
    HRESULT expectedResult = mode == 0 ? S_OK : s_failure;
    ID3D11Buffer* expectedOutput = mode == 1 ? NULL : (ID3D11Buffer*)&s_buffer;
    ULONG expectedReleases = mode == 1 ? 0 : 1;
    if (!s_active || s_checked || s_error || mode != s_mode || mode > 2
        || s_createCalls != 1 || device != (ID3D11Device*)&s_device
        || description != s_description || initialData != s_initialData
        || !CheckDescriptorBytes(description, initialData)
        || output != s_outputCell || !output || ((uintptr_t)output & 7)
        || result != expectedResult || *output != expectedOutput
        || s_buffer.references || s_buffer.releases != expectedReleases || s_buffer.lpVtbl)
    {
        return 1;
    }
    s_checked = 1;
    s_active = 0;
    return 0;
}

#if defined(NATIVE_DX11_CREATE_BUFFER_FIXTURE_SELF_TEST)
int main(void)
{
    const D3D11_BUFFER_DESC description = {
        36, D3D11_USAGE_IMMUTABLE, D3D11_BIND_VERTEX_BUFFER, 0, 0, 0
    };
    const D3D11_SUBRESOURCE_DATA initialData = { s_expectedVertices, 0, 0 };
    for (UINT mode = 0; mode < 3; mode++)
    {
        ID3D11Device* device = (ID3D11Device*)(uintptr_t)
            NativeFfiAcquireCreateBufferDevice(mode);
        ID3D11Buffer* output = NULL;
        HRESULT result = device->lpVtbl->CreateBuffer(device, &description, &initialData, &output);
        if (output)
        {
            output->lpVtbl->Release(output);
        }
        if (NativeFfiCheckCreateBuffer(mode, (uint64_t)(uintptr_t)device,
            (uint64_t)(uintptr_t)&description, (uint64_t)(uintptr_t)&initialData,
            (int32_t)result, (uint64_t)(uintptr_t)&output))
        {
            return 1;
        }
    }
    return 0;
}
#endif
