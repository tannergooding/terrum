#ifndef TERRUM_ROOT_PIN_LOADED_TYPES_V2_H
#define TERRUM_ROOT_PIN_LOADED_TYPES_V2_H

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
typedef struct RootPinObjectCellImpl* RootPinObjectCellV1;
typedef struct RootPinRootCellImpl* RootPinRootCellV1;
typedef struct RootPinPinCellImpl* RootPinPinCellV1;
typedef struct RootPinLoadedMethodImpl* RootPinLoadedMethodV1;

typedef struct RootPinTypeNodeV2
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
    uint64_t elementCount;
    uint64_t elementStride;
} RootPinTypeNodeV2;

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

typedef struct RootPinTypeBatchV2
{
    uint32_t abiRevision;
    uint32_t byteSize;
    uint32_t targetProfileId;
    uint32_t targetProfileRevision;
    uint32_t nodeCount;
    uint32_t fieldCount;
    uint32_t originCount;
    uint32_t originUseCount;
    const RootPinTypeNodeV2* nodes;
    const RootPinTypeFieldV1* fields;
    const RootPinTypeOriginV1* origins;
    const uint32_t* originUses;
    uint32_t flags;
    uint32_t reserved;
} RootPinTypeBatchV2;

typedef struct RootPinPlaceRequestV1
{
    uint64_t offset;
    uint64_t extent;
    uint64_t knownAlignment;
    RootPinLoadedTypeV1* exactT;
    uint32_t accessBits;
    uint32_t reserved;
} RootPinPlaceRequestV1;

typedef struct RootPinStableValue16V1
{
    uint64_t address;
    RootPinPinCellV1 support;
} RootPinStableValue16V1;

typedef struct RootPinSupportSlotV1
{
    void* residence;
    RootPinLoadedTypeV1* completeType;
    uint32_t live;
    uint32_t ownsHold;
} RootPinSupportSlotV1;

typedef struct RootPinSupportFrameV1
{
    struct RootPinSupportFrameV1* previous;
    uint32_t slotCount;
    uint32_t flags;
    RootPinSupportSlotV1* slots;
    RootPinLoadedMethodV1 entryMethod;
} RootPinSupportFrameV1;

typedef struct RootPinInputTransferV1
{
    RootPinSupportSlotV1* stagedSource;
    uint32_t formalOrdinal;
    uint8_t sourceRole;
    uint8_t flags;
    uint16_t reserved;
} RootPinInputTransferV1;

typedef struct RootPinCallTransferV1
{
    struct RootPinCallTransferV1* previous;
    RootPinLoadedMethodV1 target;
    RootPinSupportSlotV1* prospectiveResult;
    uint32_t inputCount;
    uint32_t state;
    RootPinInputTransferV1* inputs;
} RootPinCallTransferV1;

#if defined(TERRUM_ROOT_PIN_TESTS)
typedef struct RootPinTestMethodInputV1
{
    uint32_t formalOrdinal;
    uint32_t entrySlot;
    RootPinLoadedTypeV1* completeType;
    uint8_t sourceRole;
    uint8_t flags;
    uint16_t reserved;
} RootPinTestMethodInputV1;
#endif

enum
{
    ROOTPIN_ACCESS_READ = 1,
    ROOTPIN_ACCESS_READ_WRITE = 3
};

typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *RootPinRootExactEntryV1)(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    const RootPinPlaceRequestV1* request,
    RootPinRootCellV1* vacantOut);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *RootPinPinExactEntryV1)(
    RootPinDomainV1 domain,
    RootPinRootCellV1 root,
    RootPinStableValue16V1* vacantOut);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *RootPinPinObjectEntryV1)(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    RootPinStableValue16V1* vacantOut);
typedef uint64_t (ROOTPIN_LOADED_TYPES_CALL *RootPinResolveExactEntryV1)(
    RootPinDomainV1 domain,
    RootPinRootCellV1 root);
typedef void (ROOTPIN_LOADED_TYPES_CALL *RootPinPollEntryV1)(
    RootPinDomainV1 domain);

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
    ROOTPIN_TYPE_SHAPE_FIXED_ARRAY = 9,
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
TerrumRootPinAdmitLoadedTypesV2(
    RootPinDomainV1 domain,
    const RootPinTypeBatchV2* batch,
    RootPinLoadedTypeV1** vacantOutTypes,
    uint32_t outCount);

#if defined(TERRUM_ROOT_PIN_TESTS)
ROOTPIN_LOADED_TYPES_API RootPinDomainV1 ROOTPIN_LOADED_TYPES_CALL
RootPinTestGetDomainV1(void);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestFailAdmissionAtStageV2(uint32_t stage);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestFailRelocationAllocationAtV1(uint32_t oneBasedOrdinal);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestRegisterObjectV2(
    RootPinDomainV1 domain,
    void* payload,
    uint64_t payloadSize,
    uint64_t payloadAlignment,
    RootPinLoadedTypeV1* payloadType,
    RootPinObjectCellV1* vacantOut);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestCanRootV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    const RootPinPlaceRequestV1* request,
    uint32_t* outCanRoot);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestCanPinObjectV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    uint32_t* outCanPin);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestRootExactV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    const RootPinPlaceRequestV1* request,
    RootPinRootCellV1* vacantOut);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestPinExactV2(
    RootPinDomainV1 domain,
    RootPinRootCellV1 root,
    RootPinStableValue16V1* vacantOut);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestPinObjectV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    RootPinStableValue16V1* vacantOut);

ROOTPIN_LOADED_TYPES_API uint64_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestResolveExactV2(
    RootPinDomainV1 domain,
    RootPinRootCellV1 root);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestReplaceObjectPayloadV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    void* payload);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestReleaseRootV2(
    RootPinDomainV1 domain,
    RootPinRootCellV1 root);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestReleasePinV2(
    RootPinDomainV1 domain,
    RootPinPinCellV1 pin);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestGetObjectCountsV2(
    RootPinDomainV1 domain,
    RootPinObjectCellV1 object,
    uint64_t* rootEdges,
    uint64_t* activePins);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestRetainSupportV1(
    RootPinDomainV1 domain,
    RootPinLoadedTypeV1* completeType,
    const void* value);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestReleaseSupportV1(
    RootPinDomainV1 domain,
    RootPinLoadedTypeV1* completeType,
    const void* value);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestRegisterLoadedMethodV1(
    RootPinDomainV1 domain,
    uint64_t methodIdentity,
    RootPinLoadedTypeV1* resultType,
    const RootPinTestMethodInputV1* inputs,
    uint32_t inputCount,
    RootPinLoadedMethodV1* vacantOut);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestEnterFrameV1(
    RootPinDomainV1 domain,
    RootPinSupportFrameV1* frame);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestExitFrameV1(
    RootPinDomainV1 domain,
    RootPinSupportFrameV1* frame);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestPollV1(
    RootPinDomainV1 domain);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestBeginCallV1(
    RootPinDomainV1 domain,
    RootPinCallTransferV1* transfer);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestReturnSupportV1(
    RootPinDomainV1 domain,
    RootPinSupportSlotV1* savedResult);

ROOTPIN_LOADED_TYPES_API void ROOTPIN_LOADED_TYPES_CALL
RootPinTestFinishCallV1(
    RootPinDomainV1 domain,
    RootPinCallTransferV1* transfer,
    uint32_t completion);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestTraceCurrentFramesV1(
    RootPinDomainV1 domain,
    RootPinObjectCellV1* objects,
    uint32_t capacity,
    uint32_t* outCount);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestTraceAllFramesV1(
    RootPinDomainV1 domain,
    RootPinObjectCellV1* objects,
    uint32_t capacity,
    uint32_t* outCount);

ROOTPIN_LOADED_TYPES_API uint32_t ROOTPIN_LOADED_TYPES_CALL
RootPinTestRelocateReachableV1(
    RootPinDomainV1 domain,
    uint32_t* outMovedCount);
#endif

#ifdef __cplusplus
}
#endif

#endif
