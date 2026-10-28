#ifndef TERRUM_ROOT_PIN_LOADED_TYPES_V1_H
#define TERRUM_ROOT_PIN_LOADED_TYPES_V1_H

#include <stdint.h>

#if defined(_WIN32)
#define ROOTPIN_LOADED_TYPES_CALL __cdecl
#if defined(TERRUM_ROOT_PIN_EXPORTS)
#define ROOTPIN_LOADED_TYPES_API __declspec(dllexport)
#else
#define ROOTPIN_LOADED_TYPES_API
#endif
#else
#error The RootPin loaded-type interface is selected only for Windows x64.
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct RootPinDomainImpl* RootPinDomainV1;
typedef struct RootPinLoadedTypeV1 RootPinLoadedTypeV1;

typedef struct RootPinTypeNodeV1
{
    uint64_t typeKey;
    uint32_t role;
    uint32_t shape;
    uint64_t size;
    uint64_t alignment;
    uint64_t retirementKey;
    uint32_t child;
    uint32_t fieldFirst;
    uint32_t fieldCount;
    uint32_t originFirst;
    uint32_t originCount;
    uint32_t retirementOrigin;
    uint64_t retireGcPayloadThunk;
    uint32_t layoutKind;
    uint32_t flags;
} RootPinTypeNodeV1;

typedef struct RootPinTypeFieldV1
{
    uint64_t fieldKey;
    uint64_t offset;
    uint64_t knownAlignment;
    uint32_t valueNode;
    uint32_t reserved;
} RootPinTypeFieldV1;

typedef void (ROOTPIN_LOADED_TYPES_CALL *RootPinOriginCallbackV1)(void* cookie);

typedef struct RootPinTypeOriginV1
{
    uint64_t originKey;
    uint64_t cookie;
    RootPinOriginCallbackV1 retainOrigin;
    RootPinOriginCallbackV1 releaseOrigin;
    uint32_t kind;
    uint32_t reserved;
} RootPinTypeOriginV1;

typedef struct RootPinTypeBatchV1
{
    uint32_t abiRevision;
    uint32_t byteSize;
    uint32_t targetProfileId;
    uint32_t targetProfileRevision;
    uint32_t nodeCount;
    uint32_t fieldCount;
    uint32_t originCount;
    uint32_t originUseCount;
    const RootPinTypeNodeV1* nodes;
    const RootPinTypeFieldV1* fields;
    const RootPinTypeOriginV1* origins;
    const uint32_t* originUses;
    uint32_t flags;
    uint32_t reserved;
} RootPinTypeBatchV1;

enum
{
    ROOTPIN_TYPE_STATUS_SUCCESS = 0,
    ROOTPIN_TYPE_STATUS_EXHAUSTED = 1,
    ROOTPIN_TYPE_STATUS_INVALID = 2,
    ROOTPIN_TYPE_STATUS_UNSUPPORTED = 3,
    ROOTPIN_TYPE_ROLE_VALUE = 1,
    ROOTPIN_TYPE_ROLE_GC_PAYLOAD = 2,
    ROOTPIN_TYPE_SHAPE_LEAF = 1,
    ROOTPIN_TYPE_SHAPE_FIELDS = 2,
    ROOTPIN_TYPE_SHAPE_OPTIONAL = 3,
    ROOTPIN_TYPE_SHAPE_OBJECT_CELL = 4,
    ROOTPIN_TYPE_SHAPE_ROOT_CELL = 5,
    ROOTPIN_TYPE_SHAPE_STABLE_REF = 6,
    ROOTPIN_TYPE_SHAPE_STABLE_CLASS = 7,
    ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD = 8,
    ROOTPIN_TYPE_LAYOUT_AUTO = 0,
    ROOTPIN_TYPE_LAYOUT_SEQUENTIAL = 1,
    ROOTPIN_TYPE_LAYOUT_UNION = 2,
    ROOTPIN_TYPE_LAYOUT_TRANSPARENT = 3,
    ROOTPIN_TYPE_ORIGIN_SEMANTIC_LAYOUT = 1,
    ROOTPIN_TYPE_ORIGIN_CODE_UNWIND = 2,
    ROOTPIN_TYPE_ORIGIN_ALLOCATOR_RUNTIME = 3,
    ROOTPIN_TYPE_FLAG_COPY = 0x00000001,
    ROOTPIN_TYPE_NO_INDEX = UINT32_MAX
};

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
TerrumRootPinAdmitLoadedTypesV1(
    RootPinDomainV1 domain,
    const RootPinTypeBatchV1* batch,
    RootPinLoadedTypeV1** vacantOutTypes,
    uint32_t outCount);

#if defined(TERRUM_ROOT_PIN_TESTS)
ROOTPIN_LOADED_TYPES_API RootPinDomainV1 ROOTPIN_LOADED_TYPES_CALL
RootPinTestGetDomainV1(void);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestFailAdmissionAtStageV1(uint32_t stage);
#endif

#ifdef __cplusplus
}
#endif

#endif
