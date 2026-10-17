#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <intrin.h>
#include <windows.h>
#include <d3d11.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This fixture requires the Windows x64 ABI.
#endif

typedef struct FixtureDevice { const ID3D11DeviceVtbl* lpVtbl; } FixtureDevice;
typedef struct FixtureInputLayout
{
    const ID3D11InputLayoutVtbl* lpVtbl;
    ULONG references;
    ULONG releases;
} FixtureInputLayout;

typedef char layout_hresult_signed[((HRESULT)-1 < 0 && sizeof(HRESULT) == 4) ? 1 : -1];
typedef char layout_size_t_width[(sizeof(SIZE_T) == 8) ? 1 : -1];
typedef char layout_count_width[(sizeof(UINT) == 4) ? 1 : -1];
typedef char layout_output_width[(sizeof(ID3D11InputLayout*) == 8) ? 1 : -1];
typedef char layout_output_alignment[(__alignof(ID3D11InputLayout*) == 8) ? 1 : -1];
typedef char layout_create_slot[(offsetof(ID3D11DeviceVtbl, CreateInputLayout) == 88) ? 1 : -1];
typedef char layout_release_slot[(offsetof(ID3D11InputLayoutVtbl, Release) == 16) ? 1 : -1];
typedef char layout_release_width[(sizeof(ULONG) == 4) ? 1 : -1];
typedef char layout_descriptor_size[(sizeof(D3D11_INPUT_ELEMENT_DESC) == 32) ? 1 : -1];
typedef char layout_descriptor_alignment[(__alignof(D3D11_INPUT_ELEMENT_DESC) == 8) ? 1 : -1];

/* Arbitrary immutable fixture bytes, not DXBC or a real shader signature. */
static const unsigned char s_signature[16] = {
    0x31, 0x42, 0x53, 0x64, 0x75, 0x86, 0x97, 0xA8,
    0xB9, 0xCA, 0xDB, 0xEC, 0xFD, 0x0E, 0x1F, 0x20,
};
static ID3D11DeviceVtbl s_deviceVtbl;
static ID3D11InputLayoutVtbl s_layoutVtbl;
static __declspec(thread) FixtureDevice s_device;
static __declspec(thread) FixtureInputLayout s_layout;
static __declspec(thread) UINT s_mode;
static __declspec(thread) UINT s_active;
static __declspec(thread) UINT s_calls;
static __declspec(thread) UINT s_signatureCalls;
static __declspec(thread) UINT s_published;
static __declspec(thread) D3D11_INPUT_ELEMENT_DESC s_descriptionSnapshot;
static __declspec(thread) char s_semanticSnapshot[9];
static __declspec(thread) unsigned char s_signatureSnapshot[sizeof(s_signature)];
static const HRESULT s_failure = (HRESULT)0x80004005L;

static int IsReadonlyImageRange(const void* address, SIZE_T length)
{
    MEMORY_BASIC_INFORMATION memory;
    uintptr_t offset;
    if (!length || VirtualQuery(address, &memory, sizeof(memory)) != sizeof(memory)
        || memory.State != MEM_COMMIT || memory.Type != MEM_IMAGE || memory.Protect != PAGE_READONLY
        || (uintptr_t)address < (uintptr_t)memory.BaseAddress)
    {
        return 0;
    }
    offset = (uintptr_t)address - (uintptr_t)memory.BaseAddress;
    return offset <= memory.RegionSize && length <= memory.RegionSize - offset;
}

static __declspec(noinline) uint64_t ClobberVolatile(
    uint64_t first, uint64_t second, uint64_t third, uint64_t fourth)
{
    volatile uint64_t value = first ^ second ^ third ^ fourth;
    return value;
}

static ULONG STDMETHODCALLTYPE FixtureRelease(ID3D11InputLayout* layout)
{
    if (!s_active || layout != (ID3D11InputLayout*)&s_layout || s_layout.references != 1
        || s_layout.releases || s_layout.lpVtbl != &s_layoutVtbl
        || (s_mode != 2 && !s_published))
    {
        __fastfail(7);
    }
    s_layout.references = 0;
    s_layout.releases = 1;
    s_layout.lpVtbl = NULL;
    (void)ClobberVolatile(17, 29, 41, 53);
    return 0;
}

static __declspec(noinline) HRESULT STDMETHODCALLTYPE FixtureCreateInputLayout(ID3D11Device* device,
    const D3D11_INPUT_ELEMENT_DESC* description, UINT count, const void* signature,
    SIZE_T signatureLength, ID3D11InputLayout** output)
{
    const uintptr_t* entryStack = (const uintptr_t*)_AddressOfReturnAddress();
    /* Callee stack includes the return address: caller +32/+40 become +40/+48. */
    if (((uintptr_t)entryStack & 15) != 8
        || entryStack[5] != signatureLength || entryStack[6] != (uintptr_t)output
        || !s_active || s_calls++ || s_signatureCalls != 1
        || device != (ID3D11Device*)&s_device || device->lpVtbl != &s_deviceVtbl
        || !description || ((uintptr_t)description & 7) || count != 1
        || signature != s_signature || signatureLength != sizeof(s_signature)
        || !IsReadonlyImageRange(signature, signatureLength)
        || !output || ((uintptr_t)output & 7) || *output
        || (uintptr_t)output < (uintptr_t)(entryStack + 7)
        || (uintptr_t)output == (uintptr_t)description || (const void*)output == signature
        || !description->SemanticName || !IsReadonlyImageRange(description->SemanticName, 9)
        || memcmp(description->SemanticName, "POSITION", 9)
        || description->SemanticIndex || description->Format != DXGI_FORMAT_R32G32B32_FLOAT
        || description->InputSlot || description->AlignedByteOffset
        || description->InputSlotClass != D3D11_INPUT_PER_VERTEX_DATA
        || description->InstanceDataStepRate)
    {
        __fastfail(7);
    }
    /* Snapshot synchronously; retain no descriptor, string, signature or output-cell address. */
    s_descriptionSnapshot = *description;
    s_descriptionSnapshot.SemanticName = NULL;
    memcpy(s_semanticSnapshot, description->SemanticName, sizeof(s_semanticSnapshot));
    memcpy(s_signatureSnapshot, signature, sizeof(s_signatureSnapshot));
    if (s_mode != 1 && s_mode != 4)
    {
        s_layout.lpVtbl = &s_layoutVtbl;
        s_layout.references = 1;
        *output = (ID3D11InputLayout*)&s_layout;
    }
    (void)ClobberVolatile(67, 79, 83, 97);
    return s_mode == 1 || s_mode == 2 ? s_failure : s_mode == 3 ? (HRESULT)7 : S_OK;
}

__declspec(dllexport) uint64_t WINAPI NativeFfiAcquireInputLayoutOwnerDevice(UINT mode)
{
    HRESULT (STDMETHODCALLTYPE *create)(ID3D11Device*, const D3D11_INPUT_ELEMENT_DESC*,
        UINT, const void*, SIZE_T, ID3D11InputLayout**) = FixtureCreateInputLayout;
    ULONG (STDMETHODCALLTYPE *release)(ID3D11InputLayout*) = FixtureRelease;
    if (mode > 4 || s_layout.references || (s_active
        && (!s_published || s_layout.releases != (s_mode == 1 ? 0u : 1u))))
    {
        __fastfail(7);
    }
    memset(&s_layout, 0, sizeof(s_layout));
    s_deviceVtbl.CreateInputLayout = create;
    s_layoutVtbl.Release = release;
    s_device.lpVtbl = &s_deviceVtbl;
    s_mode = mode;
    s_active = 1;
    s_calls = 0;
    s_signatureCalls = 0;
    s_published = 0;
    return (uint64_t)(uintptr_t)&s_device;
}

__declspec(dllexport) uint64_t WINAPI NativeFfiInputLayoutOwnerSignature(void)
{
    if (!s_active || s_calls || s_signatureCalls++)
    {
        __fastfail(7);
    }
    return (uint64_t)(uintptr_t)s_signature;
}

__declspec(dllexport) void WINAPI NativeFfiCheckInputLayoutOwnerPublication(
    UINT mode, int32_t result, uint64_t output)
{
    HRESULT expected = mode == 1 || mode == 2 ? s_failure : mode == 3 ? (HRESULT)7 : S_OK;
    UINT success = mode == 0 || mode == 3;
    if (!s_active || s_published || s_calls != 1 || s_signatureCalls != 1 || mode != s_mode || mode == 4
        || result != expected
        || output != (mode == 1 ? 0 : (uint64_t)(uintptr_t)&s_layout)
        || s_layout.references != success || s_layout.releases != (mode == 2 ? 1u : 0u)
        || s_descriptionSnapshot.SemanticName || s_descriptionSnapshot.SemanticIndex
        || s_descriptionSnapshot.Format != DXGI_FORMAT_R32G32B32_FLOAT
        || s_descriptionSnapshot.InputSlot || s_descriptionSnapshot.AlignedByteOffset
        || s_descriptionSnapshot.InputSlotClass != D3D11_INPUT_PER_VERTEX_DATA
        || s_descriptionSnapshot.InstanceDataStepRate
        || memcmp(s_semanticSnapshot, "POSITION", sizeof(s_semanticSnapshot))
        || memcmp(s_signatureSnapshot, s_signature, sizeof(s_signatureSnapshot)))
    {
        __fastfail(7);
    }
    s_published = 1;
    (void)ClobberVolatile(101, 103, 107, 109);
}

__declspec(dllexport) int32_t WINAPI NativeFfiCheckInputLayoutOwnerState(int32_t references, int32_t releases)
{
    if (!s_active || !s_published || s_calls != 1 || references < 0 || releases < 0
        || s_layout.references != (ULONG)references || s_layout.releases != (ULONG)releases
        || (references != 0 && s_layout.lpVtbl != &s_layoutVtbl)
        || (references == 0 && s_layout.lpVtbl))
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

#if defined(NATIVE_DX11_INPUT_LAYOUT_OWNER_SELF_TEST)
int main(void)
{
    const D3D11_INPUT_ELEMENT_DESC description = {
        "POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0,
    };
    for (UINT mode = 0; mode < 4; mode++)
    {
        ID3D11Device* device = (ID3D11Device*)(uintptr_t)NativeFfiAcquireInputLayoutOwnerDevice(mode);
        const void* signature = (const void*)(uintptr_t)NativeFfiInputLayoutOwnerSignature();
        ID3D11InputLayout* output = NULL;
        HRESULT result = device->lpVtbl->CreateInputLayout(device, &description, 1,
            signature, sizeof(s_signature), &output);
        if (result < 0 && output)
        {
            output->lpVtbl->Release(output);
        }
        NativeFfiCheckInputLayoutOwnerPublication(mode, result, (uint64_t)(uintptr_t)output);
        if (result >= 0)
        {
            if (NativeFfiCheckInputLayoutOwnerState(1, 0))
            {
                return 1;
            }
            output->lpVtbl->Release(output);
        }
        if (NativeFfiCheckInputLayoutOwnerState(0, mode == 1 ? 0 : 1))
        {
            return 2;
        }
    }
    return 0;
}
#endif
