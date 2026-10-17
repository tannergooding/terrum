#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <windows.h>
#include <d3d11.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This fixture requires the Windows x64 ABI.
#endif

typedef struct FixtureDevice { const ID3D11DeviceVtbl* lpVtbl; } FixtureDevice;
typedef struct FixtureBuffer
{
    const ID3D11BufferVtbl* lpVtbl;
    ULONG references;
    ULONG releases;
} FixtureBuffer;

typedef char owner_hresult_signed[((HRESULT)-1 < 0 && sizeof(HRESULT) == 4) ? 1 : -1];
typedef char owner_output_width[(sizeof(ID3D11Buffer*) == 8) ? 1 : -1];
typedef char owner_create_slot[(offsetof(ID3D11DeviceVtbl, CreateBuffer) == 24) ? 1 : -1];
typedef char owner_release_slot[(offsetof(ID3D11BufferVtbl, Release) == 16) ? 1 : -1];
typedef char owner_release_width[(sizeof(ULONG) == 4) ? 1 : -1];

static ID3D11DeviceVtbl s_deviceVtbl;
static ID3D11BufferVtbl s_bufferVtbl;
static __declspec(thread) FixtureDevice s_device;
static __declspec(thread) FixtureBuffer s_buffer;
static __declspec(thread) UINT s_mode;
static __declspec(thread) UINT s_active;
static __declspec(thread) UINT s_calls;
static __declspec(thread) UINT s_published;
static __declspec(thread) D3D11_BUFFER_DESC s_descriptionSnapshot;
static __declspec(thread) UINT s_pitchSnapshot;
static __declspec(thread) UINT s_slicePitchSnapshot;
static __declspec(thread) float s_verticesSnapshot[9];
static const float s_expectedVertices[9] = {
    0.0f, 0.6f, 0.5f, 0.6f, -0.5f, 0.5f, -0.6f, -0.5f, 0.5f,
};
static const D3D11_BUFFER_DESC s_expectedDescription = {
    36, D3D11_USAGE_IMMUTABLE, D3D11_BIND_VERTEX_BUFFER, 0, 0, 0
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
    if (!s_active || buffer != (ID3D11Buffer*)&s_buffer || s_buffer.references != 1
        || s_buffer.releases || s_buffer.lpVtbl != &s_bufferVtbl
        || (s_mode != 2 && !s_published))
    {
        __fastfail(7);
    }
    s_buffer.references = 0;
    s_buffer.releases = 1;
    s_buffer.lpVtbl = NULL;
    (void)ClobberVolatile(17, 29, 41, 53);
    return 0;
}

static HRESULT STDMETHODCALLTYPE FixtureCreateBuffer(ID3D11Device* device,
    const D3D11_BUFFER_DESC* description, const D3D11_SUBRESOURCE_DATA* initialData,
    ID3D11Buffer** output)
{
    D3D11_SUBRESOURCE_DATA expectedData;
    if (!s_active || s_calls++ || device != (ID3D11Device*)&s_device
        || !description || !initialData || !output || ((uintptr_t)output & 7) || *output
        || ((uintptr_t)description & 3) || ((uintptr_t)initialData & 7)
        || memcmp(description, &s_expectedDescription, sizeof(*description))
        || !initialData->pSysMem || ((uintptr_t)initialData->pSysMem & 3))
    {
        __fastfail(7);
    }
    expectedData.pSysMem = initialData->pSysMem;
    expectedData.SysMemPitch = 0;
    expectedData.SysMemSlicePitch = 0;
    if (memcmp(initialData, &expectedData, sizeof(expectedData))
        || memcmp(initialData->pSysMem, s_expectedVertices, sizeof(s_expectedVertices)))
    {
        __fastfail(7);
    }
    /* Snapshot synchronously; no descriptor, backing, or output-cell address survives this call. */
    memcpy(&s_descriptionSnapshot, description, sizeof(s_descriptionSnapshot));
    s_pitchSnapshot = initialData->SysMemPitch;
    s_slicePitchSnapshot = initialData->SysMemSlicePitch;
    memcpy(s_verticesSnapshot, initialData->pSysMem, sizeof(s_verticesSnapshot));
    if (s_mode != 1 && s_mode != 4)
    {
        s_buffer.lpVtbl = &s_bufferVtbl;
        s_buffer.references = 1;
        *output = (ID3D11Buffer*)&s_buffer;
    }
    (void)ClobberVolatile(67, 79, 83, 97);
    return s_mode == 1 || s_mode == 2 ? s_failure : s_mode == 3 ? (HRESULT)7 : S_OK;
}

__declspec(dllexport) uint64_t WINAPI NativeFfiAcquireBufferOwnerDevice(UINT mode)
{
    if (mode > 4 || s_buffer.references || (s_active
        && (!s_published || s_buffer.releases != (s_mode == 1 ? 0u : 1u))))
    {
        __fastfail(7);
    }
    memset(&s_buffer, 0, sizeof(s_buffer));
    s_deviceVtbl.CreateBuffer = FixtureCreateBuffer;
    s_bufferVtbl.Release = FixtureRelease;
    s_device.lpVtbl = &s_deviceVtbl;
    s_mode = mode;
    s_active = 1;
    s_calls = 0;
    s_published = 0;
    return (uint64_t)(uintptr_t)&s_device;
}

__declspec(dllexport) void WINAPI NativeFfiCheckBufferOwnerPublication(
    UINT mode, int32_t result, uint64_t output)
{
    HRESULT expected = mode == 1 || mode == 2 ? s_failure : mode == 3 ? (HRESULT)7 : S_OK;
    UINT success = mode == 0 || mode == 3;
    if (!s_active || s_published || s_calls != 1 || mode != s_mode || mode == 4
        || result != expected
        || output != (mode == 1 ? 0 : (uint64_t)(uintptr_t)&s_buffer)
        || s_buffer.references != success || s_buffer.releases != (mode == 2 ? 1u : 0u)
        || memcmp(&s_descriptionSnapshot, &s_expectedDescription, sizeof(s_descriptionSnapshot))
        || s_pitchSnapshot || s_slicePitchSnapshot
        || memcmp(s_verticesSnapshot, s_expectedVertices, sizeof(s_verticesSnapshot)))
    {
        __fastfail(7);
    }
    s_published = 1;
    (void)ClobberVolatile(101, 103, 107, 109);
}

__declspec(dllexport) int32_t WINAPI NativeFfiCheckBufferOwnerState(int32_t references, int32_t releases)
{
    if (!s_active || !s_published || s_calls != 1 || references < 0 || releases < 0
        || s_buffer.references != (ULONG)references || s_buffer.releases != (ULONG)releases
        || (references != 0 && s_buffer.lpVtbl != &s_bufferVtbl)
        || (references == 0 && s_buffer.lpVtbl))
    {
        return 1;
    }
    if (references == 0)
    {
        s_active = 0;
    }
    (void)ClobberVolatile(113, 127, 131, 137);
    return 0;
}

#if defined(NATIVE_DX11_BUFFER_OWNER_SELF_TEST)
int main(void)
{
    const D3D11_SUBRESOURCE_DATA data = { s_expectedVertices, 0, 0 };
    for (UINT mode = 0; mode < 4; mode++)
    {
        ID3D11Device* device = (ID3D11Device*)(uintptr_t)NativeFfiAcquireBufferOwnerDevice(mode);
        ID3D11Buffer* output = NULL;
        HRESULT result = device->lpVtbl->CreateBuffer(device, &s_expectedDescription, &data, &output);
        if (result < 0 && output)
        {
            output->lpVtbl->Release(output);
        }
        NativeFfiCheckBufferOwnerPublication(mode, result, (uint64_t)(uintptr_t)output);
        if (result >= 0)
        {
            if (NativeFfiCheckBufferOwnerState(1, 0))
            {
                return 1;
            }
            output->lpVtbl->Release(output);
        }
        if (NativeFfiCheckBufferOwnerState(0, mode == 1 ? 0 : 1))
        {
            return 2;
        }
    }
    return 0;
}
#endif
