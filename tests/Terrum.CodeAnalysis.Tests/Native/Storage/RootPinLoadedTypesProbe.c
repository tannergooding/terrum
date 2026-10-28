#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>

#include "RootPinLoadedTypesV2.h"

typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *AdmitFunction)(
    RootPinDomainV1,
    const RootPinTypeBatchV2*,
    RootPinLoadedTypeV1**,
    uint32_t);
typedef RootPinDomainV1 (ROOTPIN_LOADED_TYPES_CALL *GetDomainFunction)(void);
typedef void (ROOTPIN_LOADED_TYPES_CALL *SetFailureFunction)(uint32_t);
typedef void (ROOTPIN_LOADED_TYPES_CALL *RetireFunction)(RootPinDomainV1, void*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *OriginCallback)(void*);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *RegisterObjectFunction)(
    RootPinDomainV1, void*, uint64_t, uint64_t, RootPinLoadedTypeV1*,
    RootPinObjectCellV1*);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *CanRootFunction)(
    RootPinDomainV1, RootPinObjectCellV1, const RootPinPlaceRequestV1*, uint32_t*);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *CanPinObjectFunction)(
    RootPinDomainV1, RootPinObjectCellV1, uint32_t*);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *RootExactFunction)(
    RootPinDomainV1, RootPinObjectCellV1, const RootPinPlaceRequestV1*,
    RootPinRootCellV1*);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *PinExactFunction)(
    RootPinDomainV1, RootPinRootCellV1, RootPinStableValue16V1*);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *PinObjectFunction)(
    RootPinDomainV1, RootPinObjectCellV1, RootPinStableValue16V1*);
typedef uint64_t (ROOTPIN_LOADED_TYPES_CALL *ResolveExactFunction)(
    RootPinDomainV1, RootPinRootCellV1);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *ReplacePayloadFunction)(
    RootPinDomainV1, RootPinObjectCellV1, void*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *ReleaseRootFunction)(
    RootPinDomainV1, RootPinRootCellV1);
typedef void (ROOTPIN_LOADED_TYPES_CALL *ReleasePinFunction)(
    RootPinDomainV1, RootPinPinCellV1);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *GetObjectCountsFunction)(
    RootPinDomainV1, RootPinObjectCellV1, uint64_t*, uint64_t*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *SupportValueFunction)(
    RootPinDomainV1, RootPinLoadedTypeV1*, const void*);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *RegisterLoadedMethodFunction)(
    RootPinDomainV1, uint64_t, RootPinLoadedTypeV1*,
    const RootPinTestMethodInputV1*, uint32_t, RootPinLoadedMethodV1*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *EnterFrameFunction)(
    RootPinDomainV1, RootPinSupportFrameV1*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *ExitFrameFunction)(
    RootPinDomainV1, RootPinSupportFrameV1*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *BeginCallFunction)(
    RootPinDomainV1, RootPinCallTransferV1*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *ReturnSupportFunction)(
    RootPinDomainV1, RootPinSupportSlotV1*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *FinishCallFunction)(
    RootPinDomainV1, RootPinCallTransferV1*, uint32_t);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *TraceCurrentFramesFunction)(
    RootPinDomainV1, RootPinObjectCellV1*, uint32_t, uint32_t*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *PollFunction)(RootPinDomainV1);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *TraceAllFramesFunction)(
    RootPinDomainV1, RootPinObjectCellV1*, uint32_t, uint32_t*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *FailRelocationFunction)(uint32_t);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *RelocateReachableFunction)(
    RootPinDomainV1, uint32_t*);

typedef struct RootPinTestMutatorContext
{
    RootPinDomainV1 domain;
    RootPinLoadedTypeV1* completeType;
    RootPinObjectCellV1 object;
    EnterFrameFunction enterFrame;
    ExitFrameFunction exitFrame;
    PollFunction poll;
    HANDLE ready;
    volatile LONG stop;
    volatile LONG polls;
    volatile LONG failed;
} RootPinTestMutatorContext;

#define CHECK(expression) \
    do { \
        if (!(expression)) { \
            fprintf(stderr, "RootPin loaded-type probe failed at line %d.\n", __LINE__); \
            return 1; \
        } \
    } while (0)

static void RetainOrigin(void* cookie)
{
    uint32_t* count = (uint32_t*)cookie;
    (*count)++;
}

static void ReleaseOrigin(void* cookie)
{
    uint32_t* count = (uint32_t*)cookie;
    (*count)--;
}

static uint32_t s_retirementCount;

static void RetirePayload(RootPinDomainV1 domain, void* payload)
{
    (void)domain;
    (void)payload;
    s_retirementCount++;
}

static DWORD WINAPI RootPinTestMutatorThread(void* parameter)
{
    RootPinTestMutatorContext* context = (RootPinTestMutatorContext*)parameter;
    RootPinObjectCellV1 rootedObject = context->object;
    RootPinSupportSlotV1 slot = {
        &rootedObject, context->completeType, 1, 0 };
    RootPinSupportFrameV1 frame = { NULL, 1, 0, &slot, NULL };
    context->enterFrame(context->domain, &frame);
    if (!SetEvent(context->ready))
    {
        InterlockedExchange(&context->failed, 1);
    }

    while (InterlockedCompareExchange(&context->stop, 0, 0) == 0)
    {
        context->poll(context->domain);
        InterlockedIncrement(&context->polls);
        SwitchToThread();
    }

    context->exitFrame(context->domain, &frame);
    return 0;
}

static int WaitForMutatorPolls(
    RootPinTestMutatorContext* first,
    RootPinTestMutatorContext* second,
    LONG target)
{
    const ULONGLONG start = GetTickCount64();
    while (InterlockedCompareExchange(&first->polls, 0, 0) < target
        || InterlockedCompareExchange(&second->polls, 0, 0) < target)
    {
        if (GetTickCount64() - start >= 10000)
        {
            return 0;
        }
        SwitchToThread();
    }
    return InterlockedCompareExchange(&first->failed, 0, 0) == 0
        && InterlockedCompareExchange(&second->failed, 0, 0) == 0;
}

static int RunConcurrentMutatorChecks(
    RootPinDomainV1 domain,
    RootPinLoadedTypeV1* objectType,
    RootPinObjectCellV1 objectA,
    RootPinObjectCellV1 objectB,
    EnterFrameFunction enterFrame,
    ExitFrameFunction exitFrame,
    PollFunction poll,
    TraceAllFramesFunction traceAllFrames)
{
    RootPinTestMutatorContext contexts[2] = { 0 };
    HANDLE threads[2] = { NULL, NULL };
    RootPinObjectCellV1 tracedObjects[8] = { 0 };
    uint32_t tracedCount = 0;
    int result = 1;

    contexts[0].domain = domain;
    contexts[0].completeType = objectType;
    contexts[0].object = objectA;
    contexts[0].enterFrame = enterFrame;
    contexts[0].exitFrame = exitFrame;
    contexts[0].poll = poll;
    contexts[1].domain = domain;
    contexts[1].completeType = objectType;
    contexts[1].object = objectB;
    contexts[1].enterFrame = enterFrame;
    contexts[1].exitFrame = exitFrame;
    contexts[1].poll = poll;

    for (uint32_t index = 0; index < 2; index++)
    {
        contexts[index].ready = CreateEventA(NULL, TRUE, FALSE, NULL);
        if (contexts[index].ready == NULL)
        {
            goto cleanup;
        }
        threads[index] = CreateThread(NULL, 0, RootPinTestMutatorThread,
            &contexts[index], 0, NULL);
        if (threads[index] == NULL)
        {
            goto cleanup;
        }
    }

    for (uint32_t index = 0; index < 2; index++)
    {
        if (WaitForSingleObject(contexts[index].ready, 10000) != WAIT_OBJECT_0)
        {
            goto cleanup;
        }
    }

    for (LONG iteration = 1; iteration <= 4; iteration++)
    {
        if (traceAllFrames(domain, tracedObjects, 8, &tracedCount)
                != ROOTPIN_TYPE_STATUS_SUCCESS
            || tracedCount != 2)
        {
            goto cleanup;
        }

        int foundA = 0;
        int foundB = 0;
        for (uint32_t index = 0; index < tracedCount; index++)
        {
            foundA |= tracedObjects[index] == objectA;
            foundB |= tracedObjects[index] == objectB;
        }
        if (!foundA || !foundB
            || !WaitForMutatorPolls(&contexts[0], &contexts[1], iteration))
        {
            goto cleanup;
        }
    }

    result = 0;

cleanup:
    for (uint32_t index = 0; index < 2; index++)
    {
        InterlockedExchange(&contexts[index].stop, 1);
    }
    for (uint32_t index = 0; index < 2; index++)
    {
        if (threads[index] != NULL)
        {
            if (WaitForSingleObject(threads[index], 10000) != WAIT_OBJECT_0)
            {
                result = 1;
            }
            CloseHandle(threads[index]);
        }
        if (contexts[index].ready != NULL)
        {
            CloseHandle(contexts[index].ready);
        }
    }
    return result;
}

static void SetNode(
    RootPinTypeNodeV2* node,
    uint64_t typeKey,
    uint32_t role,
    uint32_t shape,
    uint64_t size,
    uint64_t alignment,
    uint32_t child,
    uint32_t fieldFirst,
    uint32_t fieldCount,
    uint32_t originFirst,
    uint32_t originCount)
{
    node->typeKey = typeKey;
    node->role = role;
    node->shape = shape;
    node->size = size;
    node->alignment = alignment;
    node->child = child;
    node->fieldFirst = fieldFirst;
    node->fieldCount = fieldCount;
    node->originFirst = originFirst;
    node->originCount = originCount;
    node->retirementOrigin = (uint32_t)ROOTPIN_TYPE_NO_INDEX;
    node->flags = (uint32_t)ROOTPIN_TYPE_FLAG_COPY;
    node->elementCount = 0;
    node->elementStride = 0;
}

static void SetOrigin(
    RootPinTypeOriginV1* origin,
    uint64_t key,
    uint64_t cookie,
    uint32_t kind,
    OriginCallback retain,
    OriginCallback release)
{
    origin->originKey = key;
    origin->cookie = cookie;
    origin->retainOrigin = retain;
    origin->releaseOrigin = release;
    origin->kind = kind;
}

static void SetPlaceRequest(
    RootPinPlaceRequestV1* request,
    uint64_t offset,
    uint64_t extent,
    uint64_t knownAlignment,
    RootPinLoadedTypeV1* exactType,
    uint32_t accessBits)
{
    request->offset = offset;
    request->extent = extent;
    request->knownAlignment = knownAlignment;
    request->exactT = exactType;
    request->accessBits = accessBits;
    request->reserved = 0;
}

#pragma pack(push, 1)
typedef struct RootPinTestPackedV2
{
    uint8_t tag;
    int32_t value;
} RootPinTestPackedV2;
#pragma pack(pop)

typedef struct RootPinTestPayloadV2
{
    uint32_t values[6];
    RootPinObjectCellV1 next;
    RootPinTestPackedV2 packed;
} RootPinTestPayloadV2;

typedef struct RootPinTestSupportPayloadV1
{
    RootPinObjectCellV1 next;
    RootPinRootCellV1 root;
} RootPinTestSupportPayloadV1;

typedef struct RootPinTestSupportAggregateV1
{
    RootPinRootCellV1 firstRoot;
    RootPinRootCellV1 secondRoot;
    RootPinStableValue16V1 optionalStable;
    RootPinRootCellV1 roots[2];
    RootPinStableValue16V1 optionalStableClass;
} RootPinTestSupportAggregateV1;

typedef char RootPinPackedValueSizeCheck[
    sizeof(((RootPinTestPayloadV2*)0)->packed) == 5 ? 1 : -1];
typedef char RootPinPayloadPackedOffsetCheck[
    offsetof(RootPinTestPayloadV2, packed) == 32 ? 1 : -1];

static void BuildValidBatch(
    RootPinTypeBatchV2* batch,
    RootPinTypeNodeV2* nodes,
    RootPinTypeFieldV1* fields,
    RootPinTypeOriginV1* origins,
    uint32_t* originUses,
    uint32_t* originCounts)
{
    memset(nodes, 0, sizeof(RootPinTypeNodeV2) * 19);
    memset(fields, 0, sizeof(RootPinTypeFieldV1) * 8);
    memset(origins, 0, sizeof(RootPinTypeOriginV1) * 3);

    SetNode(&nodes[0], 1000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_LEAF, 4, 4,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 0, 0, 1);
    SetNode(&nodes[1], 2000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 8, 4,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 2, 1, 1);
    SetNode(&nodes[2], 3000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_OBJECT_CELL, 8, 8,
        3, 0, 0, 2, 1);
    SetNode(&nodes[3], 4000, (uint32_t)ROOTPIN_TYPE_ROLE_GC_PAYLOAD,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 40, 8,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 2, 3, 3, 3);
    SetNode(&nodes[4], 5000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_ROOT_CELL, 8, 8,
        0, 0, 0, 6, 1);
    SetNode(&nodes[5], 6000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_STABLE_REF, 16, 8,
        0, 0, 0, 7, 1);
    SetNode(&nodes[6], 7000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_OPTIONAL, 16, 8,
        5, 0, 0, 8, 1);
    SetNode(&nodes[7], 1000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_LEAF, 4, 4,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 0, 9, 1);
    SetNode(&nodes[8], 8000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_STABLE_CLASS, 16, 8,
        3, 0, 0, 10, 1);
    SetNode(&nodes[9], 9000, (uint32_t)ROOTPIN_TYPE_ROLE_GC_PAYLOAD,
        (uint32_t)ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD, 4, 4,
        0, 0, 0, 11, 3);
    SetNode(&nodes[10], 10000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_OBJECT_CELL, 8, 8,
        9, 0, 0, 14, 1);
    SetNode(&nodes[11], 11000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 4, 4,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 5, 2, 15, 1);
    SetNode(&nodes[12], 12001, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 5, 1,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 7, 1, 16, 1);
    SetNode(&nodes[13], 14000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIXED_ARRAY, 24, 4,
        1, 0, 0, 17, 1);
    nodes[13].elementCount = 3;
    nodes[13].elementStride = 8;
    SetNode(&nodes[14], 14001, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIXED_ARRAY, 48, 4,
        13, 0, 0, 18, 1);
    nodes[14].elementCount = 2;
    nodes[14].elementStride = 24;
    SetNode(&nodes[15], 14002, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIXED_ARRAY, 0, 4,
        0, 0, 0, 19, 1);
    nodes[15].elementCount = 0;
    nodes[15].elementStride = 4;
    SetNode(&nodes[16], 14003, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_LEAF, 0, 0,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 0, 20, 1);
    SetNode(&nodes[17], 14004, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIXED_ARRAY, 0, 1,
        16, 0, 0, 21, 1);
    nodes[17].elementCount = UINT64_MAX;
    nodes[17].elementStride = 0;
    SetNode(&nodes[18], 14000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIXED_ARRAY, 24, 4,
        1, 0, 0, 22, 1);
    nodes[18].elementCount = 3;
    nodes[18].elementStride = 8;

    nodes[1].layoutKind = ROOTPIN_TYPE_LAYOUT_SEQUENTIAL;
    nodes[3].layoutKind = ROOTPIN_TYPE_LAYOUT_SEQUENTIAL;
    nodes[3].flags = 0;
    nodes[3].retirementKey = 500;
    nodes[3].retireGcPayloadThunk = (uint64_t)(uintptr_t)&RetirePayload;
    nodes[3].retirementOrigin = 1;
    nodes[9].flags = 0;
    nodes[9].retirementKey = 501;
    nodes[9].retireGcPayloadThunk = (uint64_t)(uintptr_t)&RetirePayload;
    nodes[9].retirementOrigin = 1;
    nodes[11].layoutKind = ROOTPIN_TYPE_LAYOUT_UNION;
    nodes[12].layoutKind = ROOTPIN_TYPE_LAYOUT_SEQUENTIAL;

    fields[0].fieldKey = 101;
    fields[0].offset = 0;
    fields[0].knownAlignment = 4;
    fields[0].valueNode = 0;
    fields[1].fieldKey = 102;
    fields[1].offset = 4;
    fields[1].knownAlignment = 4;
    fields[1].valueNode = 0;
    fields[2].fieldKey = 201;
    fields[2].offset = 0;
    fields[2].knownAlignment = 4;
    fields[2].valueNode = 13;
    fields[3].fieldKey = 202;
    fields[3].offset = 24;
    fields[3].knownAlignment = 8;
    fields[3].valueNode = 2;
    fields[4].fieldKey = 203;
    fields[4].offset = 32;
    fields[4].knownAlignment = 1;
    fields[4].valueNode = 12;
    fields[5].fieldKey = 301;
    fields[5].offset = 0;
    fields[5].knownAlignment = 4;
    fields[5].valueNode = 0;
    fields[6].fieldKey = 302;
    fields[6].offset = 0;
    fields[6].knownAlignment = 4;
    fields[6].valueNode = 0;
    fields[7].fieldKey = 401;
    fields[7].offset = 1;
    fields[7].knownAlignment = 1;
    fields[7].valueNode = 0;

    SetOrigin(&origins[0], 10, (uint64_t)(uintptr_t)&originCounts[0],
        (uint32_t)ROOTPIN_TYPE_ORIGIN_SEMANTIC_LAYOUT, RetainOrigin, ReleaseOrigin);
    SetOrigin(&origins[1], 20, (uint64_t)(uintptr_t)&originCounts[1],
        (uint32_t)ROOTPIN_TYPE_ORIGIN_CODE_UNWIND, RetainOrigin, ReleaseOrigin);
    SetOrigin(&origins[2], 30, (uint64_t)(uintptr_t)&originCounts[2],
        (uint32_t)ROOTPIN_TYPE_ORIGIN_ALLOCATOR_RUNTIME, RetainOrigin, ReleaseOrigin);

    originUses[0] = 0;
    originUses[1] = 0;
    originUses[2] = 0;
    originUses[3] = 0;
    originUses[4] = 1;
    originUses[5] = 2;
    originUses[6] = 0;
    originUses[7] = 0;
    originUses[8] = 0;
    originUses[9] = 0;
    originUses[10] = 0;
    originUses[11] = 0;
    originUses[12] = 1;
    originUses[13] = 2;
    originUses[14] = 0;
    originUses[15] = 0;
    originUses[16] = 0;
    originUses[17] = 0;
    originUses[18] = 0;
    originUses[19] = 0;
    originUses[20] = 0;
    originUses[21] = 0;
    originUses[22] = 0;

    batch->abiRevision = 2;
    batch->byteSize = sizeof(*batch);
    batch->targetProfileId = 2;
    batch->targetProfileRevision = 1;
    batch->nodeCount = 19;
    batch->fieldCount = 8;
    batch->originCount = 3;
    batch->originUseCount = 23;
    batch->nodes = nodes;
    batch->fields = fields;
    batch->origins = origins;
    batch->originUses = originUses;
    batch->flags = 0;
    batch->reserved = 0;
}

static int OutputsAreZero(RootPinLoadedTypeV1** outputs, uint32_t count)
{
    uint32_t index;
    for (index = 0; index < count; index++)
    {
        if (outputs[index] != NULL)
        {
            return 0;
        }
    }
    return 1;
}

#define LOAD_FUNCTION(exportName, functionVariable) \
    do { \
        FARPROC functionAddress = GetProcAddress(library, exportName); \
        CHECK(functionAddress != NULL && sizeof(functionVariable) == sizeof(functionAddress)); \
        memcpy(&(functionVariable), &functionAddress, sizeof(functionVariable)); \
    } while (0)

static int RunCellCases(
    HMODULE library,
    RootPinDomainV1 domain,
    RootPinLoadedTypeV1** types,
    SetFailureFunction setFailure)
{
    RegisterObjectFunction registerObject = NULL;
    CanRootFunction canRoot = NULL;
    CanPinObjectFunction canPinObject = NULL;
    RootExactFunction rootExact = NULL;
    PinExactFunction pinExact = NULL;
    PinObjectFunction pinObject = NULL;
    ResolveExactFunction resolveExact = NULL;
    ReplacePayloadFunction replacePayload = NULL;
    ReleaseRootFunction releaseRoot = NULL;
    ReleasePinFunction releasePin = NULL;
    GetObjectCountsFunction getObjectCounts = NULL;
    RootPinTestPayloadV2 payload = { 0 };
    RootPinTestPayloadV2 movedPayload = { 0 };
    int32_t boxStorage = 42;
    RootPinObjectCellV1 object = NULL;
    RootPinObjectCellV1 boxObject = NULL;
    RootPinRootCellV1 root = NULL;
    RootPinRootCellV1 rootArray = NULL;
    RootPinRootCellV1 rootPair = NULL;
    RootPinRootCellV1 rootSlot = NULL;
    RootPinRootCellV1 packedRoot = NULL;
    RootPinRootCellV1 movedRoot = NULL;
    RootPinRootCellV1 boxRoot = NULL;
    RootPinStableValue16V1 firstPin = { 0 };
    RootPinStableValue16V1 secondPin = { 0 };
    RootPinStableValue16V1 objectPin = { 0 };
    RootPinStableValue16V1 movedPin = { 0 };
    RootPinStableValue16V1 boxPin = { 0 };
    RootPinStableValue16V1 packedPin = { 0 };
    RootPinPlaceRequestV1 request;
    uint64_t rootEdges;
    uint64_t activePins;
    uint32_t canSelect;
    uint32_t status;

    LOAD_FUNCTION("RootPinTestRegisterObjectV2", registerObject);
    LOAD_FUNCTION("RootPinTestCanRootV2", canRoot);
    LOAD_FUNCTION("RootPinTestCanPinObjectV2", canPinObject);
    LOAD_FUNCTION("RootPinTestRootExactV2", rootExact);
    LOAD_FUNCTION("RootPinTestPinExactV2", pinExact);
    LOAD_FUNCTION("RootPinTestPinObjectV2", pinObject);
    LOAD_FUNCTION("RootPinTestResolveExactV2", resolveExact);
    LOAD_FUNCTION("RootPinTestReplaceObjectPayloadV2", replacePayload);
    LOAD_FUNCTION("RootPinTestReleaseRootV2", releaseRoot);
    LOAD_FUNCTION("RootPinTestReleasePinV2", releasePin);
    LOAD_FUNCTION("RootPinTestGetObjectCountsV2", getObjectCounts);

    CHECK(sizeof(payload) == 40
        && offsetof(RootPinTestPayloadV2, packed) == 32
        && offsetof(RootPinTestPackedV2, value) == 1);
    payload.values[0] = 10;
    payload.values[1] = 11;
    payload.values[2] = 20;
    payload.values[3] = 21;
    payload.values[4] = 30;
    payload.values[5] = 31;
    status = registerObject(domain, &payload, sizeof(payload), 8, types[3], &object);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && object != NULL);
    payload.next = object;
    movedPayload = payload;
    status = canPinObject(domain, object, &canSelect);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && canSelect == 1);
    setFailure((uint32_t)6);
    status = pinObject(domain, object, &objectPin);
    CHECK(status == ROOTPIN_TYPE_STATUS_EXHAUSTED
        && objectPin.address == 0 && objectPin.support == NULL);
    status = getObjectCounts(domain, object, &rootEdges, &activePins);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 0 && activePins == 0);

    SetPlaceRequest(&request, 20, 4, 4, types[0], (uint32_t)ROOTPIN_ACCESS_READ_WRITE);
    status = canRoot(domain, object, &request, &canSelect);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && canSelect == 1);
    status = rootExact(domain, object, &request, &root);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && root != NULL);
    CHECK(resolveExact(domain, root) == (uint64_t)(uintptr_t)&payload.values[5]);

    SetPlaceRequest(&request, 0, 24, 4, types[13], (uint32_t)ROOTPIN_ACCESS_READ);
    status = canRoot(domain, object, &request, &canSelect);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && canSelect == 1);
    status = rootExact(domain, object, &request, &rootArray);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootArray != NULL);
    CHECK(resolveExact(domain, rootArray) == (uint64_t)(uintptr_t)&payload.values[0]);
    releaseRoot(domain, rootArray);

    SetPlaceRequest(&request, 8, 8, 4, types[1], (uint32_t)ROOTPIN_ACCESS_READ);
    status = rootExact(domain, object, &request, &rootPair);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootPair != NULL);
    CHECK(resolveExact(domain, rootPair) == (uint64_t)(uintptr_t)&payload.values[2]);
    releaseRoot(domain, rootPair);

    SetPlaceRequest(&request, 32, 4, 4, types[0], (uint32_t)ROOTPIN_ACCESS_READ);
    status = canRoot(domain, object, &request, &canSelect);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && canSelect == 0);
    SetPlaceRequest(&request, 20, 8, 4, types[1], (uint32_t)ROOTPIN_ACCESS_READ);
    status = canRoot(domain, object, &request, &canSelect);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && canSelect == 0);
    SetPlaceRequest(&request, 20, 4, 8, types[0], (uint32_t)ROOTPIN_ACCESS_READ);
    status = canRoot(domain, object, &request, &canSelect);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && canSelect == 0);
    SetPlaceRequest(&request, 33, 4, 4, types[0], (uint32_t)ROOTPIN_ACCESS_READ);
    status = canRoot(domain, object, &request, &canSelect);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && canSelect == 0);
    SetPlaceRequest(&request, 33, 4, 1, types[0], (uint32_t)ROOTPIN_ACCESS_READ);
    status = canRoot(domain, object, &request, &canSelect);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && canSelect == 1);
    status = rootExact(domain, object, &request, &packedRoot);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS
        && resolveExact(domain, packedRoot) == (uint64_t)(uintptr_t)&payload.packed.value);
    status = pinExact(domain, packedRoot, &packedPin);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS
        && packedPin.address == (uint64_t)(uintptr_t)&payload.packed.value);
    releasePin(domain, packedPin.support);
    releaseRoot(domain, packedRoot);
    status = getObjectCounts(domain, object, &rootEdges, &activePins);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 0);
    SetPlaceRequest(&request, 20, 4, 4, types[0], 2);
    status = canRoot(domain, object, &request, &canSelect);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && canSelect == 0);
    SetPlaceRequest(&request, 20, 4, 4, types[0], (uint32_t)ROOTPIN_ACCESS_READ_WRITE);

    status = getObjectCounts(domain, object, &rootEdges, &activePins);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 0);
    setFailure((uint32_t)4);
    RootPinRootCellV1 failedRoot = NULL;
    status = rootExact(domain, object, &request, &failedRoot);
    CHECK(status == ROOTPIN_TYPE_STATUS_EXHAUSTED && failedRoot == NULL);
    status = getObjectCounts(domain, object, &rootEdges, &activePins);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 0);

    setFailure((uint32_t)5);
    status = pinExact(domain, root, &firstPin);
    CHECK(status == ROOTPIN_TYPE_STATUS_EXHAUSTED
        && firstPin.address == 0 && firstPin.support == NULL);
    status = getObjectCounts(domain, object, &rootEdges, &activePins);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 0);

    status = pinExact(domain, root, &firstPin);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS
        && firstPin.address == (uint64_t)(uintptr_t)&payload.values[5]
        && firstPin.support != NULL);
    status = pinExact(domain, root, &secondPin);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS
        && secondPin.address == firstPin.address
        && secondPin.support != NULL && secondPin.support != firstPin.support);
    status = getObjectCounts(domain, object, &rootEdges, &activePins);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 2);
    status = replacePayload(domain, object, &movedPayload);
    CHECK(status == ROOTPIN_TYPE_STATUS_UNSUPPORTED);
    releaseRoot(domain, root);
    status = getObjectCounts(domain, object, &rootEdges, &activePins);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 2);
    releasePin(domain, firstPin.support);
    status = getObjectCounts(domain, object, &rootEdges, &activePins);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 1);
    releasePin(domain, secondPin.support);
    status = getObjectCounts(domain, object, &rootEdges, &activePins);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 0 && activePins == 0);

    SetPlaceRequest(&request, 20, 4, 4, types[0], (uint32_t)ROOTPIN_ACCESS_READ);
    status = rootExact(domain, object, &request, &movedRoot);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && movedRoot != root
        && resolveExact(domain, movedRoot) == (uint64_t)(uintptr_t)&payload.values[5]);
    status = replacePayload(domain, object, &movedPayload);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS
        && resolveExact(domain, movedRoot) == (uint64_t)(uintptr_t)&movedPayload.values[5]);
    status = pinExact(domain, movedRoot, &movedPin);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS
        && movedPin.address == (uint64_t)(uintptr_t)&movedPayload.values[5]);
    status = replacePayload(domain, object, &payload);
    CHECK(status == ROOTPIN_TYPE_STATUS_UNSUPPORTED
        && resolveExact(domain, movedRoot) == movedPin.address);
    releasePin(domain, movedPin.support);
    releaseRoot(domain, movedRoot);

    status = pinObject(domain, object, &objectPin);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS
        && objectPin.address == (uint64_t)(uintptr_t)&movedPayload
        && objectPin.support != NULL);
    status = replacePayload(domain, object, &payload);
    CHECK(status == ROOTPIN_TYPE_STATUS_UNSUPPORTED);
    releasePin(domain, objectPin.support);

    SetPlaceRequest(&request, 24, 8, 8, types[2], (uint32_t)ROOTPIN_ACCESS_READ);
    status = rootExact(domain, object, &request, &rootSlot);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS
        && resolveExact(domain, rootSlot) == (uint64_t)(uintptr_t)&movedPayload.next);
    movedPayload.next = NULL;
    CHECK(resolveExact(domain, rootSlot) == (uint64_t)(uintptr_t)&movedPayload.next);
    releaseRoot(domain, rootSlot);

    status = registerObject(domain, &boxStorage, sizeof(boxStorage), 4, types[9], &boxObject);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && boxObject != NULL);
    status = canPinObject(domain, boxObject, &canSelect);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && canSelect == 0);
    SetPlaceRequest(&request, 0, 4, 4, types[0], (uint32_t)ROOTPIN_ACCESS_READ);
    status = rootExact(domain, boxObject, &request, &boxRoot);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS
        && resolveExact(domain, boxRoot) == (uint64_t)(uintptr_t)&boxStorage);
    status = pinExact(domain, boxRoot, &boxPin);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS
        && boxPin.address == (uint64_t)(uintptr_t)&boxStorage);
    releasePin(domain, boxPin.support);
    releaseRoot(domain, boxRoot);

    status = getObjectCounts(domain, object, &rootEdges, &activePins);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 0 && activePins == 0);
    return 0;
}

static int RunDeepSupportWalk(
    AdmitFunction admit,
    RootPinDomainV1 domain,
    SupportValueFunction retainSupport,
    SupportValueFunction releaseSupport,
    uint32_t* originCounts,
    RootPinRootCellV1 root)
{
    enum
    {
        DeepLayers = 512,
        DeepNodeCount = DeepLayers + 4,
        DeepFieldCount = DeepLayers + 2,
        DeepOriginUseCount = DeepNodeCount + 2,
    };
    RootPinTypeBatchV2 batch = { 0 };
    RootPinTypeNodeV2 nodes[DeepNodeCount] = { 0 };
    RootPinTypeFieldV1 fields[DeepFieldCount] = { 0 };
    RootPinTypeOriginV1 origins[3] = { 0 };
    uint32_t originUses[DeepOriginUseCount] = { 0 };
    RootPinLoadedTypeV1* types[DeepNodeCount] = { 0 };
    uint8_t value[sizeof(RootPinRootCellV1)];

    SetNode(&nodes[0], 15000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_LEAF, 4, 4,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 0, 3, 1);
    SetNode(&nodes[1], 15001, (uint32_t)ROOTPIN_TYPE_ROLE_GC_PAYLOAD,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 16, 8,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 2, 0, 3);
    nodes[1].flags = 0;
    nodes[1].retirementKey = 502;
    nodes[1].retireGcPayloadThunk = (uint64_t)(uintptr_t)&RetirePayload;
    nodes[1].retirementOrigin = 1;
    nodes[1].layoutKind = ROOTPIN_TYPE_LAYOUT_SEQUENTIAL;
    SetNode(&nodes[2], 15002, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_OBJECT_CELL, 8, 8,
        1, 0, 0, 4, 1);
    SetNode(&nodes[3], 15003, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_ROOT_CELL, 8, 8,
        2, 0, 0, 5, 1);

    fields[0].fieldKey = 15011;
    fields[0].offset = 0;
    fields[0].knownAlignment = 8;
    fields[0].valueNode = 2;
    fields[1].fieldKey = 15012;
    fields[1].offset = 8;
    fields[1].knownAlignment = 8;
    fields[1].valueNode = 3;

    for (uint32_t layer = 0; layer < DeepLayers; layer++)
    {
        const uint32_t nodeIndex = layer + 4;
        const uint32_t fieldIndex = layer + 2;
        SetNode(&nodes[nodeIndex], 16000 + layer, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
            (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 8, 8,
            (uint32_t)ROOTPIN_TYPE_NO_INDEX, fieldIndex, 1, nodeIndex + 2, 1);
        nodes[nodeIndex].layoutKind = ROOTPIN_TYPE_LAYOUT_SEQUENTIAL;
        fields[fieldIndex].fieldKey = 16000 + layer;
        fields[fieldIndex].offset = 0;
        fields[fieldIndex].knownAlignment = 8;
        fields[fieldIndex].valueNode = nodeIndex - 1;
    }

    SetOrigin(&origins[0], 10, (uint64_t)(uintptr_t)&originCounts[0],
        (uint32_t)ROOTPIN_TYPE_ORIGIN_SEMANTIC_LAYOUT, RetainOrigin, ReleaseOrigin);
    SetOrigin(&origins[1], 20, (uint64_t)(uintptr_t)&originCounts[1],
        (uint32_t)ROOTPIN_TYPE_ORIGIN_CODE_UNWIND, RetainOrigin, ReleaseOrigin);
    SetOrigin(&origins[2], 30, (uint64_t)(uintptr_t)&originCounts[2],
        (uint32_t)ROOTPIN_TYPE_ORIGIN_ALLOCATOR_RUNTIME, RetainOrigin, ReleaseOrigin);
    originUses[0] = 0;
    originUses[1] = 1;
    originUses[2] = 2;
    for (uint32_t index = 3; index < DeepOriginUseCount; index++)
    {
        originUses[index] = 0;
    }

    batch.abiRevision = 2;
    batch.byteSize = sizeof(batch);
    batch.targetProfileId = 2;
    batch.targetProfileRevision = 1;
    batch.nodeCount = DeepNodeCount;
    batch.fieldCount = DeepFieldCount;
    batch.originCount = 3;
    batch.originUseCount = DeepOriginUseCount;
    batch.nodes = nodes;
    batch.fields = fields;
    batch.origins = origins;
    batch.originUses = originUses;
    CHECK(admit(domain, &batch, types, DeepNodeCount) == ROOTPIN_TYPE_STATUS_SUCCESS);
    memcpy(value, &root, sizeof(root));
    retainSupport(domain, types[DeepNodeCount - 1], value);
    releaseSupport(domain, types[DeepNodeCount - 1], value);
    CHECK(originCounts[0] == 1 && originCounts[1] == 1 && originCounts[2] == 1);
    return 0;
}

static int RunSupportCases(
    HMODULE library,
    RootPinDomainV1 domain,
    AdmitFunction admit,
    uint32_t* originCounts)
{
    RegisterObjectFunction registerObject = NULL;
    RootExactFunction rootExact = NULL;
    PinExactFunction pinExact = NULL;
    PinObjectFunction pinObject = NULL;
    ReleaseRootFunction releaseRoot = NULL;
    GetObjectCountsFunction getObjectCounts = NULL;
    SupportValueFunction retainSupport = NULL;
    SupportValueFunction releaseSupport = NULL;
    RegisterLoadedMethodFunction registerMethod = NULL;
    EnterFrameFunction enterFrame = NULL;
    ExitFrameFunction exitFrame = NULL;
    BeginCallFunction beginCall = NULL;
    ReturnSupportFunction returnSupport = NULL;
    FinishCallFunction finishCall = NULL;
    TraceCurrentFramesFunction traceCurrentFrames = NULL;
    ResolveExactFunction resolveExact = NULL;
    PollFunction poll = NULL;
    TraceAllFramesFunction traceAllFrames = NULL;
    FailRelocationFunction failRelocation = NULL;
    RelocateReachableFunction relocateReachable = NULL;
    RootPinTypeBatchV2 batch = { 0 };
    RootPinTypeNodeV2 nodes[10] = { 0 };
    RootPinTypeFieldV1 fields[7] = { 0 };
    RootPinTypeOriginV1 origins[3] = { 0 };
    uint32_t originUses[12] = { 0 };
    RootPinLoadedTypeV1* types[10] = { 0 };
    RootPinTestSupportPayloadV1 payloadA = { 0 };
    RootPinTestSupportPayloadV1 payloadB = { 0 };
    RootPinTestSupportPayloadV1 unreachablePayload = { 0 };
    RootPinObjectCellV1 objectA = NULL;
    RootPinObjectCellV1 objectB = NULL;
    RootPinObjectCellV1 unreachableObject = NULL;
    RootPinObjectCellV1 rootedObject = NULL;
    RootPinObjectCellV1 tracedObjects[8] = { 0 };
    RootPinRootCellV1 rootA = NULL;
    RootPinRootCellV1 rootB = NULL;
    RootPinRootCellV1 slotRoot = NULL;
    RootPinRootCellV1 relocatedPlaceRoot = NULL;
    RootPinStableValue16V1 stable = { 0 };
    RootPinStableValue16V1 stableClass = { 0 };
    RootPinStableValue16V1 movementPin = { 0 };
    RootPinStableValue16V1 overlappingMovementPin = { 0 };
    RootPinStableValue16V1 oldStable = { 0 };
    RootPinStableValue16V1 newStable = { 0 };
    RootPinStableValue16V1 destination = { 0 };
    RootPinStableValue16V1 oldDestination = { 0 };
    RootPinStableValue16V1 absent = { 0 };
    RootPinTestSupportAggregateV1 aggregate = { 0 };
    RootPinSupportSlotV1 traceSlot;
    RootPinSupportFrameV1 traceFrame = { 0 };
    RootPinTestMethodInputV1 methodInput = { 0 };
    RootPinLoadedMethodV1 method = NULL;
    RootPinSupportSlotV1 callerSlots[3];
    RootPinSupportFrameV1 callerFrame = { 0 };
    RootPinSupportSlotV1 calleeSlots[2];
    RootPinSupportFrameV1 calleeFrame = { 0 };
    RootPinInputTransferV1 callInput;
    RootPinCallTransferV1 call;
    RootPinRootCellV1 calleeRoot = NULL;
    RootPinStableValue16V1 callerResult = { 0 };
    RootPinStableValue16V1 calleeResult = { 0 };
    RootPinRootCellV1 failureRoot = NULL;
    RootPinSupportSlotV1 failureSlots[3];
    RootPinSupportFrameV1 failureFrame = { 0 };
    RootPinRootCellV1 exceptionalRoot = NULL;
    RootPinSupportSlotV1 exceptionalCallerSlots[3];
    RootPinSupportFrameV1 exceptionalCallerFrame = { 0 };
    RootPinSupportSlotV1 exceptionalCalleeSlots[1];
    RootPinSupportFrameV1 exceptionalCalleeFrame = { 0 };
    RootPinInputTransferV1 exceptionalInput;
    RootPinCallTransferV1 exceptionalCall;
    RootPinRootCellV1 exceptionalCalleeRoot = NULL;
    RootPinPlaceRequestV1 request;
    uint64_t rootEdges;
    uint64_t activePins;
    uint64_t objectABase;
    uint64_t objectBBase;
    uint64_t placeAddress;
    RootPinObjectCellV1 nextObject;
    uint32_t traceCount;
    uint32_t movedCount;
    uint32_t status;

    LOAD_FUNCTION("RootPinTestRegisterObjectV2", registerObject);
    LOAD_FUNCTION("RootPinTestRootExactV2", rootExact);
    LOAD_FUNCTION("RootPinTestPinExactV2", pinExact);
    LOAD_FUNCTION("RootPinTestPinObjectV2", pinObject);
    LOAD_FUNCTION("RootPinTestReleaseRootV2", releaseRoot);
    LOAD_FUNCTION("RootPinTestGetObjectCountsV2", getObjectCounts);
    LOAD_FUNCTION("RootPinTestRetainSupportV1", retainSupport);
    LOAD_FUNCTION("RootPinTestReleaseSupportV1", releaseSupport);
    LOAD_FUNCTION("RootPinTestRegisterLoadedMethodV1", registerMethod);
    LOAD_FUNCTION("RootPinTestEnterFrameV1", enterFrame);
    LOAD_FUNCTION("RootPinTestExitFrameV1", exitFrame);
    LOAD_FUNCTION("RootPinTestBeginCallV1", beginCall);
    LOAD_FUNCTION("RootPinTestReturnSupportV1", returnSupport);
    LOAD_FUNCTION("RootPinTestFinishCallV1", finishCall);
    LOAD_FUNCTION("RootPinTestTraceCurrentFramesV1", traceCurrentFrames);
    LOAD_FUNCTION("RootPinTestResolveExactV2", resolveExact);
    LOAD_FUNCTION("RootPinTestPollV1", poll);
    LOAD_FUNCTION("RootPinTestTraceAllFramesV1", traceAllFrames);
    LOAD_FUNCTION("RootPinTestFailRelocationAllocationAtV1", failRelocation);
    LOAD_FUNCTION("RootPinTestRelocateReachableV1", relocateReachable);

    SetNode(&nodes[0], 15000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_LEAF, 4, 4,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 0, 3, 1);
    SetNode(&nodes[1], 15001, (uint32_t)ROOTPIN_TYPE_ROLE_GC_PAYLOAD,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 16, 8,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 2, 0, 3);
    SetNode(&nodes[2], 15002, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_OBJECT_CELL, 8, 8,
        1, 0, 0, 4, 1);
    SetNode(&nodes[3], 15003, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_ROOT_CELL, 8, 8,
        2, 0, 0, 5, 1);
    SetNode(&nodes[4], 15004, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_STABLE_REF, 16, 8,
        2, 0, 0, 6, 1);
    SetNode(&nodes[5], 15005, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_OPTIONAL, 16, 8,
        4, 0, 0, 7, 1);
    SetNode(&nodes[6], 15006, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIXED_ARRAY, 16, 8,
        3, 0, 0, 8, 1);
    nodes[6].elementCount = 2;
    nodes[6].elementStride = 8;
    SetNode(&nodes[7], 15007, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 64, 8,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 2, 5, 9, 1);
    SetNode(&nodes[8], 15008, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_STABLE_CLASS, 16, 8,
        1, 0, 0, 10, 1);
    SetNode(&nodes[9], 15009, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_OPTIONAL, 16, 8,
        8, 0, 0, 11, 1);
    nodes[1].flags = 0;
    nodes[1].retirementKey = 502;
    nodes[1].retireGcPayloadThunk = (uint64_t)(uintptr_t)&RetirePayload;
    nodes[1].retirementOrigin = 1;
    nodes[1].layoutKind = ROOTPIN_TYPE_LAYOUT_SEQUENTIAL;
    nodes[7].layoutKind = ROOTPIN_TYPE_LAYOUT_SEQUENTIAL;

    fields[0].fieldKey = 15011;
    fields[0].offset = 0;
    fields[0].knownAlignment = 8;
    fields[0].valueNode = 2;
    fields[1].fieldKey = 15012;
    fields[1].offset = 8;
    fields[1].knownAlignment = 8;
    fields[1].valueNode = 3;
    fields[2].fieldKey = 15013;
    fields[2].offset = 0;
    fields[2].knownAlignment = 8;
    fields[2].valueNode = 3;
    fields[3].fieldKey = 15014;
    fields[3].offset = 8;
    fields[3].knownAlignment = 8;
    fields[3].valueNode = 3;
    fields[4].fieldKey = 15015;
    fields[4].offset = 16;
    fields[4].knownAlignment = 8;
    fields[4].valueNode = 5;
    fields[5].fieldKey = 15016;
    fields[5].offset = 32;
    fields[5].knownAlignment = 8;
    fields[5].valueNode = 6;
    fields[6].fieldKey = 15017;
    fields[6].offset = 48;
    fields[6].knownAlignment = 8;
    fields[6].valueNode = 9;

    SetOrigin(&origins[0], 10, (uint64_t)(uintptr_t)&originCounts[0],
        (uint32_t)ROOTPIN_TYPE_ORIGIN_SEMANTIC_LAYOUT, RetainOrigin, ReleaseOrigin);
    SetOrigin(&origins[1], 20, (uint64_t)(uintptr_t)&originCounts[1],
        (uint32_t)ROOTPIN_TYPE_ORIGIN_CODE_UNWIND, RetainOrigin, ReleaseOrigin);
    SetOrigin(&origins[2], 30, (uint64_t)(uintptr_t)&originCounts[2],
        (uint32_t)ROOTPIN_TYPE_ORIGIN_ALLOCATOR_RUNTIME, RetainOrigin, ReleaseOrigin);
    originUses[0] = 0;
    originUses[1] = 1;
    originUses[2] = 2;
    for (uint32_t index = 3; index < 12; index++)
    {
        originUses[index] = 0;
    }

    batch.abiRevision = 2;
    batch.byteSize = sizeof(batch);
    batch.targetProfileId = 2;
    batch.targetProfileRevision = 1;
    batch.nodeCount = 10;
    batch.fieldCount = 7;
    batch.originCount = 3;
    batch.originUseCount = 12;
    batch.nodes = nodes;
    batch.fields = fields;
    batch.origins = origins;
    batch.originUses = originUses;
    CHECK(admit(domain, &batch, types, 10) == ROOTPIN_TYPE_STATUS_SUCCESS);
    CHECK(sizeof(RootPinTestSupportPayloadV1) == 16
        && sizeof(RootPinTestSupportAggregateV1) == 64
        && offsetof(RootPinTestSupportAggregateV1, optionalStable) == 16
        && offsetof(RootPinTestSupportAggregateV1, roots) == 32
        && offsetof(RootPinTestSupportAggregateV1, optionalStableClass) == 48);

    CHECK(registerObject(domain, &payloadA, sizeof(payloadA), 8, types[1], &objectA)
        == ROOTPIN_TYPE_STATUS_SUCCESS);
    CHECK(registerObject(domain, &payloadB, sizeof(payloadB), 8, types[1], &objectB)
        == ROOTPIN_TYPE_STATUS_SUCCESS);
    CHECK(registerObject(domain, &unreachablePayload, sizeof(unreachablePayload), 8,
        types[1], &unreachableObject) == ROOTPIN_TYPE_STATUS_SUCCESS);
    payloadA.next = objectB;
    payloadB.next = objectA;
    unreachablePayload.next = unreachableObject;

    SetPlaceRequest(&request, 0, 8, 8, types[2], (uint32_t)ROOTPIN_ACCESS_READ);
    CHECK(rootExact(domain, objectA, &request, &rootA) == ROOTPIN_TYPE_STATUS_SUCCESS);
    CHECK(rootExact(domain, objectB, &request, &rootB) == ROOTPIN_TYPE_STATUS_SUCCESS);
    payloadA.root = rootA;
    payloadB.root = rootB;
    retainSupport(domain, types[3], &payloadA.root);
    retainSupport(domain, types[3], &payloadB.root);
    releaseSupport(domain, types[3], &rootA);
    releaseSupport(domain, types[3], &rootB);

    rootedObject = objectA;
    traceSlot = (RootPinSupportSlotV1){ &rootedObject, types[2], 1, 0 };
    traceFrame = (RootPinSupportFrameV1){ NULL, 1, 0, &traceSlot, NULL };
    enterFrame(domain, &traceFrame);
    status = traceCurrentFrames(domain, tracedObjects, 8, &traceCount);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && traceCount == 2);
    CHECK((tracedObjects[0] == objectA && tracedObjects[1] == objectB)
        || (tracedObjects[0] == objectB && tracedObjects[1] == objectA));
    exitFrame(domain, &traceFrame);
    CHECK(traceCurrentFrames(domain, tracedObjects, 8, &traceCount)
        == ROOTPIN_TYPE_STATUS_SUCCESS && traceCount == 0);

    CHECK(getObjectCounts(domain, objectA, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 0);
    CHECK(getObjectCounts(domain, objectB, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 0);

    rootA = payloadA.root;
    retainSupport(domain, types[5], &absent);
    releaseSupport(domain, types[5], &absent);
    retainSupport(domain, types[9], &absent);
    releaseSupport(domain, types[9], &absent);
    CHECK(pinExact(domain, rootA, &stable) == ROOTPIN_TYPE_STATUS_SUCCESS);
    aggregate.firstRoot = rootA;
    aggregate.secondRoot = rootA;
    aggregate.optionalStable = stable;
    aggregate.roots[0] = rootA;
    aggregate.roots[1] = rootA;
    CHECK(pinObject(domain, objectA, &stableClass) == ROOTPIN_TYPE_STATUS_SUCCESS);
    aggregate.optionalStableClass = stableClass;
    retainSupport(domain, types[7], &aggregate);
    releaseSupport(domain, types[4], &stable);
    releaseSupport(domain, types[8], &stableClass);
    traceSlot = (RootPinSupportSlotV1){ &aggregate, types[7], 1, 1 };
    traceFrame = (RootPinSupportFrameV1){ NULL, 1, 0, &traceSlot, NULL };
    enterFrame(domain, &traceFrame);
    status = traceCurrentFrames(domain, tracedObjects, 8, &traceCount);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && traceCount == 2);
    exitFrame(domain, &traceFrame);
    CHECK(getObjectCounts(domain, objectA, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 0);

    CHECK(RunDeepSupportWalk(admit, domain, retainSupport, releaseSupport,
        originCounts, rootA) == 0);

    CHECK(pinExact(domain, rootA, &oldStable) == ROOTPIN_TYPE_STATUS_SUCCESS);
    destination = oldStable;
    retainSupport(domain, types[4], &destination);
    CHECK(pinExact(domain, rootB, &newStable) == ROOTPIN_TYPE_STATUS_SUCCESS);
    oldDestination = destination;
    retainSupport(domain, types[4], &newStable);
    destination = newStable;
    CHECK(getObjectCounts(domain, objectA, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && activePins == 1);
    CHECK(getObjectCounts(domain, objectB, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && activePins == 1);
    releaseSupport(domain, types[4], &oldDestination);
    releaseSupport(domain, types[4], &newStable);
    releaseSupport(domain, types[4], &oldStable);
    CHECK(getObjectCounts(domain, objectA, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && activePins == 0);
    CHECK(getObjectCounts(domain, objectB, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && activePins == 1);
    releaseSupport(domain, types[4], &destination);
    CHECK(getObjectCounts(domain, objectB, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && activePins == 0);

    methodInput.formalOrdinal = 0;
    methodInput.entrySlot = 0;
    methodInput.completeType = types[3];
    methodInput.sourceRole = 0;
    CHECK(registerMethod(domain, 1, types[4], &methodInput, 1, &method)
        == ROOTPIN_TYPE_STATUS_SUCCESS && method != NULL);

    rootedObject = objectA;
    callerSlots[0] = (RootPinSupportSlotV1){ &rootedObject, types[2], 1, 0 };
    callerSlots[1] = (RootPinSupportSlotV1){ &slotRoot, types[3], 0, 0 };
    callerSlots[2] = (RootPinSupportSlotV1){ &callerResult, types[4], 0, 0 };
    callerFrame = (RootPinSupportFrameV1){ NULL, 3, 0, callerSlots, NULL };
    enterFrame(domain, &callerFrame);
    CHECK(rootExact(domain, objectA, &request, &slotRoot) == ROOTPIN_TYPE_STATUS_SUCCESS);
    callerSlots[1].live = 1;
    callerSlots[1].ownsHold = 1;
    callInput = (RootPinInputTransferV1){ &callerSlots[1], 0, 0, 0, 0 };
    call = (RootPinCallTransferV1){ NULL, method, &callerSlots[2], 1, 0, &callInput };
    beginCall(domain, &call);

    calleeRoot = slotRoot;
    calleeSlots[0] = (RootPinSupportSlotV1){ &calleeRoot, types[3], 0, 0 };
    calleeSlots[1] = (RootPinSupportSlotV1){ &calleeResult, types[4], 0, 0 };
    calleeFrame = (RootPinSupportFrameV1){ NULL, 2, 0, calleeSlots, method };
    enterFrame(domain, &calleeFrame);
    CHECK(callerSlots[1].live == 0 && callerSlots[1].ownsHold == 0
        && calleeSlots[0].live == 1 && calleeSlots[0].ownsHold == 1);
    CHECK(pinExact(domain, calleeRoot, &calleeResult) == ROOTPIN_TYPE_STATUS_SUCCESS);
    calleeSlots[1].live = 1;
    calleeSlots[1].ownsHold = 1;
    callerResult = calleeResult;
    returnSupport(domain, &calleeSlots[1]);
    exitFrame(domain, &calleeFrame);
    finishCall(domain, &call, 1);
    CHECK(callerSlots[2].live == 1 && callerSlots[2].ownsHold == 1
        && callerResult.support != NULL);
    callerSlots[0].live = 0;
    CHECK(traceCurrentFrames(domain, tracedObjects, 8, &traceCount)
        == ROOTPIN_TYPE_STATUS_SUCCESS && traceCount == 2);
    releaseSupport(domain, types[4], &callerResult);
    memset(&callerResult, 0, sizeof(callerResult));
    callerSlots[2].live = 0;
    callerSlots[2].ownsHold = 0;
    exitFrame(domain, &callerFrame);
    CHECK(getObjectCounts(domain, objectA, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 0);

    rootedObject = objectA;
    failureSlots[0] = (RootPinSupportSlotV1){ &rootedObject, types[2], 1, 0 };
    failureSlots[1] = (RootPinSupportSlotV1){ &failureRoot, types[3], 0, 0 };
    failureSlots[2] = (RootPinSupportSlotV1){ &callerResult, types[4], 0, 0 };
    failureFrame = (RootPinSupportFrameV1){ NULL, 3, 0, failureSlots, NULL };
    enterFrame(domain, &failureFrame);
    CHECK(rootExact(domain, objectA, &request, &failureRoot) == ROOTPIN_TYPE_STATUS_SUCCESS);
    failureSlots[1].live = 1;
    failureSlots[1].ownsHold = 1;
    callInput = (RootPinInputTransferV1){ &failureSlots[1], 0, 0, 0, 0 };
    call = (RootPinCallTransferV1){ NULL, method, &failureSlots[2], 1, 0, &callInput };
    beginCall(domain, &call);
    finishCall(domain, &call, 0);
    CHECK(failureSlots[1].live == 0 && failureSlots[1].ownsHold == 0);
    exitFrame(domain, &failureFrame);
    CHECK(getObjectCounts(domain, objectA, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 0);

    rootedObject = objectA;
    exceptionalCallerSlots[0] = (RootPinSupportSlotV1){ &rootedObject, types[2], 1, 0 };
    exceptionalCallerSlots[1] = (RootPinSupportSlotV1){ &exceptionalRoot, types[3], 0, 0 };
    exceptionalCallerSlots[2] = (RootPinSupportSlotV1){ &callerResult, types[4], 0, 0 };
    exceptionalCallerFrame = (RootPinSupportFrameV1){
        NULL, 3, 0, exceptionalCallerSlots, NULL };
    enterFrame(domain, &exceptionalCallerFrame);
    CHECK(rootExact(domain, objectA, &request, &exceptionalRoot) == ROOTPIN_TYPE_STATUS_SUCCESS);
    exceptionalCallerSlots[1].live = 1;
    exceptionalCallerSlots[1].ownsHold = 1;
    exceptionalInput = (RootPinInputTransferV1){
        &exceptionalCallerSlots[1], 0, 0, 0, 0 };
    exceptionalCall = (RootPinCallTransferV1){
        NULL, method, &exceptionalCallerSlots[2], 1, 0, &exceptionalInput };
    beginCall(domain, &exceptionalCall);
    exceptionalCalleeRoot = exceptionalRoot;
    exceptionalCalleeSlots[0] = (RootPinSupportSlotV1){
        &exceptionalCalleeRoot, types[3], 0, 0 };
    exceptionalCalleeFrame = (RootPinSupportFrameV1){
        NULL, 1, 0, exceptionalCalleeSlots, method };
    enterFrame(domain, &exceptionalCalleeFrame);
    exitFrame(domain, &exceptionalCalleeFrame);
    finishCall(domain, &exceptionalCall, 0);
    exitFrame(domain, &exceptionalCallerFrame);
    CHECK(getObjectCounts(domain, objectA, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && rootEdges == 1 && activePins == 0);
    CHECK(originCounts[0] == 1 && originCounts[1] == 1 && originCounts[2] == 1);
    CHECK(s_retirementCount == 0);
    CHECK(RunConcurrentMutatorChecks(domain, types[2], objectA, objectB,
        enterFrame, exitFrame, poll, traceAllFrames) == 0);
    CHECK(traceAllFrames(domain, tracedObjects, 8, &traceCount)
        == ROOTPIN_TYPE_STATUS_SUCCESS && traceCount == 0);

    SetPlaceRequest(&request, 8, 8, 8, types[3], (uint32_t)ROOTPIN_ACCESS_READ);
    CHECK(rootExact(domain, objectA, &request, &relocatedPlaceRoot)
        == ROOTPIN_TYPE_STATUS_SUCCESS);
    rootedObject = objectA;
    traceSlot = (RootPinSupportSlotV1){ &rootedObject, types[2], 1, 0 };
    traceFrame = (RootPinSupportFrameV1){ NULL, 1, 0, &traceSlot, NULL };
    enterFrame(domain, &traceFrame);
    objectABase = resolveExact(domain, rootA);
    objectBBase = resolveExact(domain, rootB);
    placeAddress = resolveExact(domain, relocatedPlaceRoot);
    CHECK(placeAddress - objectABase == 8);

    CHECK(pinExact(domain, rootA, &movementPin) == ROOTPIN_TYPE_STATUS_SUCCESS);
    CHECK(pinExact(domain, rootA, &overlappingMovementPin) == ROOTPIN_TYPE_STATUS_SUCCESS);
    CHECK(relocateReachable(domain, &movedCount) == ROOTPIN_TYPE_STATUS_SUCCESS
        && movedCount == 1);
    CHECK(resolveExact(domain, rootA) == objectABase);
    CHECK(movementPin.address == objectABase
        && overlappingMovementPin.address == objectABase);
    CHECK(resolveExact(domain, relocatedPlaceRoot) == placeAddress);
    CHECK(resolveExact(domain, rootB) != objectBBase);
    CHECK(getObjectCounts(domain, objectA, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && activePins == 2);
    CHECK(getObjectCounts(domain, objectB, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && activePins == 0);
    memcpy(&nextObject, (const void*)(uintptr_t)resolveExact(domain, rootB),
        sizeof(nextObject));
    CHECK(nextObject == objectA);

    releaseSupport(domain, types[4], &movementPin);
    memset(&movementPin, 0, sizeof(movementPin));
    objectBBase = resolveExact(domain, rootB);
    CHECK(relocateReachable(domain, &movedCount) == ROOTPIN_TYPE_STATUS_SUCCESS
        && movedCount == 1);
    CHECK(resolveExact(domain, rootA) == objectABase
        && resolveExact(domain, rootB) != objectBBase);
    CHECK(overlappingMovementPin.address == objectABase);
    CHECK(getObjectCounts(domain, objectA, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && activePins == 1);

    releaseSupport(domain, types[4], &overlappingMovementPin);
    memset(&overlappingMovementPin, 0, sizeof(overlappingMovementPin));
    CHECK(getObjectCounts(domain, objectA, &rootEdges, &activePins)
        == ROOTPIN_TYPE_STATUS_SUCCESS && activePins == 0);
    objectABase = resolveExact(domain, rootA);
    objectBBase = resolveExact(domain, rootB);
    CHECK(relocateReachable(domain, &movedCount) == ROOTPIN_TYPE_STATUS_SUCCESS
        && movedCount == 2);
    CHECK(resolveExact(domain, rootA) != objectABase
        && resolveExact(domain, rootB) != objectBBase);
    CHECK(resolveExact(domain, relocatedPlaceRoot) - resolveExact(domain, rootA) == 8);
    memcpy(&nextObject, (const void*)(uintptr_t)resolveExact(domain, rootA),
        sizeof(nextObject));
    CHECK(nextObject == objectB);
    memcpy(&nextObject, (const void*)(uintptr_t)resolveExact(domain, rootB),
        sizeof(nextObject));
    CHECK(nextObject == objectA);

    objectABase = resolveExact(domain, rootA);
    objectBBase = resolveExact(domain, rootB);
    failRelocation(2);
    CHECK(relocateReachable(domain, &movedCount) == ROOTPIN_TYPE_STATUS_EXHAUSTED
        && movedCount == 0);
    CHECK(resolveExact(domain, rootA) == objectABase
        && resolveExact(domain, rootB) == objectBBase);
    exitFrame(domain, &traceFrame);
    releaseRoot(domain, relocatedPlaceRoot);
    CHECK(traceAllFrames(domain, tracedObjects, 8, &traceCount)
        == ROOTPIN_TYPE_STATUS_SUCCESS && traceCount == 0);
    return 0;
}

#undef LOAD_FUNCTION

static int RunProbe(const char* libraryPath, int invalidSupport)
{
    HMODULE library = LoadLibraryA(libraryPath);
    FARPROC procedure;
    AdmitFunction admit = NULL;
    GetDomainFunction getDomain = NULL;
    SetFailureFunction setFailure = NULL;
    SupportValueFunction retainSupport = NULL;
    RootPinDomainV1 domain;
    RootPinTypeBatchV2 batch;
    RootPinTypeNodeV2 nodes[19];
    RootPinTypeFieldV1 fields[8];
    RootPinTypeOriginV1 origins[3];
    uint32_t originUses[23];
    uint32_t originCounts[3] = { 0, 0, 0 };
    RootPinLoadedTypeV1* outputs[19];
    RootPinLoadedTypeV1* repeated[19];
    uint32_t status;
    uint32_t index;

    CHECK(library != NULL);

    procedure = GetProcAddress(library, "TerrumRootPinAdmitLoadedTypesV2");
    CHECK(procedure != NULL && sizeof(admit) == sizeof(procedure));
    memcpy(&admit, &procedure, sizeof(admit));
    CHECK(GetProcAddress(library, "TerrumRootPinAdmitLoadedTypesV1") == NULL);
    procedure = GetProcAddress(library, "RootPinTestGetDomainV1");
    CHECK(procedure != NULL && sizeof(getDomain) == sizeof(procedure));
    memcpy(&getDomain, &procedure, sizeof(getDomain));
    procedure = GetProcAddress(library, "RootPinTestFailAdmissionAtStageV2");
    CHECK(procedure != NULL && sizeof(setFailure) == sizeof(procedure));
    memcpy(&setFailure, &procedure, sizeof(setFailure));
    procedure = GetProcAddress(library, "RootPinTestRetainSupportV1");
    CHECK(procedure != NULL && sizeof(retainSupport) == sizeof(procedure));
    memcpy(&retainSupport, &procedure, sizeof(retainSupport));
    CHECK(admit != NULL && getDomain != NULL && setFailure != NULL && retainSupport != NULL);

    domain = getDomain();
    CHECK(domain != NULL);
    BuildValidBatch(&batch, nodes, fields, origins, originUses, originCounts);
    memset(outputs, 0xA5, sizeof(outputs));
    status = admit(domain, &batch, outputs, 19);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS);
    CHECK(outputs[0] != NULL && outputs[1] != NULL && outputs[2] != NULL
        && outputs[3] != NULL && outputs[4] != NULL && outputs[5] != NULL
        && outputs[6] != NULL && outputs[7] != NULL && outputs[8] != NULL
        && outputs[9] != NULL && outputs[10] != NULL && outputs[11] != NULL
        && outputs[12] != NULL && outputs[13] != NULL && outputs[14] != NULL
        && outputs[15] != NULL && outputs[16] != NULL && outputs[17] != NULL
        && outputs[18] != NULL);
    CHECK(outputs[0] == outputs[7]);
    CHECK(outputs[0] != outputs[1] && outputs[5] != outputs[6]);
    CHECK(outputs[8] != outputs[3] && outputs[9] != outputs[0]
        && outputs[10] != outputs[9] && outputs[11] != outputs[1]
        && outputs[12] != outputs[1] && outputs[13] != outputs[14]
        && outputs[13] == outputs[18]);
    CHECK(originCounts[0] == 1 && originCounts[1] == 1 && originCounts[2] == 1);
    if (RunCellCases(library, domain, outputs, setFailure) != 0)
    {
        return 1;
    }
    if (RunSupportCases(library, domain, admit, originCounts) != 0)
    {
        return 1;
    }

    status = admit(domain, &batch, repeated, 19);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS);
    for (index = 0; index < 19; index++)
    {
        CHECK(repeated[index] == outputs[index]);
    }
    CHECK(originCounts[0] == 1 && originCounts[1] == 1 && originCounts[2] == 1);

    nodes[7].size = 8;
    nodes[7].alignment = 8;
    memset(repeated, 0xA5, sizeof(repeated));
    status = admit(domain, &batch, repeated, 19);
    CHECK(status == ROOTPIN_TYPE_STATUS_INVALID && OutputsAreZero(repeated, 19));
    CHECK(originCounts[0] == 1 && originCounts[1] == 1 && originCounts[2] == 1);
    nodes[7].size = 4;
    nodes[7].alignment = 4;

    nodes[5].child = 99;
    memset(repeated, 0xA5, sizeof(repeated));
    status = admit(domain, &batch, repeated, 19);
    CHECK(status == ROOTPIN_TYPE_STATUS_INVALID && OutputsAreZero(repeated, 19));
    nodes[5].child = 0;

    batch.targetProfileRevision = 2;
    memset(repeated, 0xA5, sizeof(repeated));
    status = admit(domain, &batch, repeated, 19);
    CHECK(status == ROOTPIN_TYPE_STATUS_UNSUPPORTED && OutputsAreZero(repeated, 19));
    batch.targetProfileRevision = 1;

    batch.abiRevision = 1;
    memset(repeated, 0xA5, sizeof(repeated));
    status = admit(domain, &batch, repeated, 19);
    CHECK(status == ROOTPIN_TYPE_STATUS_UNSUPPORTED && OutputsAreZero(repeated, 19));
    batch.abiRevision = 2;
    fflush(stderr);

    nodes[0].elementCount = 1;
    memset(repeated, 0xA5, sizeof(repeated));
    status = admit(domain, &batch, repeated, 19);
    CHECK(status == ROOTPIN_TYPE_STATUS_INVALID && OutputsAreZero(repeated, 19));
    nodes[0].elementCount = 0;

    nodes[18].elementCount = 4;
    nodes[18].size = 32;
    memset(repeated, 0xA5, sizeof(repeated));
    status = admit(domain, &batch, repeated, 19);
    CHECK(status == ROOTPIN_TYPE_STATUS_INVALID && OutputsAreZero(repeated, 19));
    nodes[18].elementCount = 3;
    nodes[18].size = 24;

    nodes[13].elementCount = UINT64_MAX;
    memset(repeated, 0xA5, sizeof(repeated));
    status = admit(domain, &batch, repeated, 19);
    CHECK(status == ROOTPIN_TYPE_STATUS_INVALID && OutputsAreZero(repeated, 19));
    nodes[13].elementCount = 3;

    {
        RootPinTypeNodeV2 cycleNode = { 0 };
        RootPinTypeFieldV1 cycleField = { 0 };
        uint32_t cycleOriginUse = 0;
        RootPinTypeBatchV2 cycleBatch = { 0 };
        RootPinLoadedTypeV1* cycleOutput[1];

        SetNode(&cycleNode, 12000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
            (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 4, 4,
            (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 1, 0, 1);
        cycleNode.layoutKind = ROOTPIN_TYPE_LAYOUT_SEQUENTIAL;
        cycleField.fieldKey = 401;
        cycleField.offset = 0;
        cycleField.knownAlignment = 4;
        cycleField.valueNode = 0;
        cycleBatch.abiRevision = 2;
        cycleBatch.byteSize = sizeof(cycleBatch);
        cycleBatch.targetProfileId = 2;
        cycleBatch.targetProfileRevision = 1;
        cycleBatch.nodeCount = 1;
        cycleBatch.fieldCount = 1;
        cycleBatch.originCount = 1;
        cycleBatch.originUseCount = 1;
        cycleBatch.nodes = &cycleNode;
        cycleBatch.fields = &cycleField;
        cycleBatch.origins = &origins[0];
        cycleBatch.originUses = &cycleOriginUse;
        cycleOutput[0] = (RootPinLoadedTypeV1*)(uintptr_t)1;
        status = admit(domain, &cycleBatch, cycleOutput, 1);
        CHECK(status == ROOTPIN_TYPE_STATUS_INVALID && cycleOutput[0] == NULL);
        CHECK(originCounts[0] == 1);
    }

    {
        RootPinTypeNodeV2 arrayCycleNode = { 0 };
        uint32_t arrayCycleOriginUse = 0;
        RootPinTypeBatchV2 arrayCycleBatch = { 0 };
        RootPinLoadedTypeV1* arrayCycleOutput[1];

        SetNode(&arrayCycleNode, 12500, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
            (uint32_t)ROOTPIN_TYPE_SHAPE_FIXED_ARRAY, 0, 4,
            0, 0, 0, 0, 1);
        arrayCycleNode.elementCount = 0;
        arrayCycleNode.elementStride = 0;
        arrayCycleBatch.abiRevision = 2;
        arrayCycleBatch.byteSize = sizeof(arrayCycleBatch);
        arrayCycleBatch.targetProfileId = 2;
        arrayCycleBatch.targetProfileRevision = 1;
        arrayCycleBatch.nodeCount = 1;
        arrayCycleBatch.originCount = 1;
        arrayCycleBatch.originUseCount = 1;
        arrayCycleBatch.nodes = &arrayCycleNode;
        arrayCycleBatch.origins = &origins[0];
        arrayCycleBatch.originUses = &arrayCycleOriginUse;
        arrayCycleOutput[0] = (RootPinLoadedTypeV1*)(uintptr_t)1;
        status = admit(domain, &arrayCycleBatch, arrayCycleOutput, 1);
        CHECK(status == ROOTPIN_TYPE_STATUS_INVALID && arrayCycleOutput[0] == NULL);
        CHECK(originCounts[0] == 1);
    }

    {
        RootPinTypeNodeV2 payloadNodes[2] = { 0 };
        RootPinTypeOriginV1 payloadOrigins[2] = { origins[0], origins[1] };
        uint32_t payloadUses[3] = { 0, 0, 1 };
        RootPinTypeBatchV2 payloadBatch = { 0 };
        RootPinLoadedTypeV1* payloadOutputs[2];

        SetNode(&payloadNodes[0], 13000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
            (uint32_t)ROOTPIN_TYPE_SHAPE_LEAF, 4, 4,
            (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 0, 0, 1);
        SetNode(&payloadNodes[1], 13001, (uint32_t)ROOTPIN_TYPE_ROLE_GC_PAYLOAD,
            (uint32_t)ROOTPIN_TYPE_SHAPE_BOX_PAYLOAD, 4, 4,
            0, 0, 0, 1, 2);
        payloadNodes[1].flags = 0;
        payloadNodes[1].retirementKey = 601;
        payloadNodes[1].retireGcPayloadThunk = (uint64_t)(uintptr_t)&RetirePayload;
        payloadNodes[1].retirementOrigin = 1;
        payloadBatch.abiRevision = 2;
        payloadBatch.byteSize = sizeof(payloadBatch);
        payloadBatch.targetProfileId = 2;
        payloadBatch.targetProfileRevision = 1;
        payloadBatch.nodeCount = 2;
        payloadBatch.originCount = 2;
        payloadBatch.originUseCount = 3;
        payloadBatch.nodes = payloadNodes;
        payloadBatch.origins = payloadOrigins;
        payloadBatch.originUses = payloadUses;
        payloadOutputs[0] = (RootPinLoadedTypeV1*)(uintptr_t)1;
        payloadOutputs[1] = (RootPinLoadedTypeV1*)(uintptr_t)1;
        status = admit(domain, &payloadBatch, payloadOutputs, 2);
        CHECK(status == ROOTPIN_TYPE_STATUS_INVALID && OutputsAreZero(payloadOutputs, 2));
        CHECK(originCounts[0] == 1 && originCounts[1] == 1 && originCounts[2] == 1);
    }

    {
        RootPinTypeNodeV2 extraNode = { 0 };
        RootPinTypeOriginV1 extraOrigin = { 0 };
        uint32_t extraUse = 0;
        RootPinTypeBatchV2 extraBatch = { 0 };
        RootPinLoadedTypeV1* extraOutput[1];

        SetNode(&extraNode, 9000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
            (uint32_t)ROOTPIN_TYPE_SHAPE_LEAF,
            4, 4, (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 0, 0, 1);
        SetOrigin(&extraOrigin, 90, (uint64_t)(uintptr_t)&originCounts[0],
            (uint32_t)ROOTPIN_TYPE_ORIGIN_SEMANTIC_LAYOUT, RetainOrigin, ReleaseOrigin);
        extraBatch.abiRevision = 2;
        extraBatch.byteSize = sizeof(extraBatch);
        extraBatch.targetProfileId = 2;
        extraBatch.targetProfileRevision = 1;
        extraBatch.nodeCount = 1;
        extraBatch.originCount = 1;
        extraBatch.originUseCount = 1;
        extraBatch.nodes = &extraNode;
        extraBatch.origins = &extraOrigin;
        extraBatch.originUses = &extraUse;
        setFailure((uint32_t)2);
        extraOutput[0] = (RootPinLoadedTypeV1*)(uintptr_t)1;
        status = admit(domain, &extraBatch, extraOutput, 1);
        CHECK(status == ROOTPIN_TYPE_STATUS_EXHAUSTED && extraOutput[0] == NULL);
        CHECK(originCounts[0] == 1);
        status = admit(domain, &extraBatch, extraOutput, 1);
        CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS && extraOutput[0] != NULL);
        CHECK(originCounts[0] == 2);
    }
    if (invalidSupport)
    {
        RootPinRootCellV1 invalidRoot = (RootPinRootCellV1)(uintptr_t)1;
        retainSupport(domain, outputs[4], &invalidRoot);
        return 3;
    }

    puts("rootpin-loaded-types=pass");
    return 0;
}

int main(int argc, char** argv)
{
    if ((argc != 2 && argc != 3)
        || (argc == 3 && strcmp(argv[2], "invalid-support") != 0))
    {
        fprintf(stderr, "Expected the provider DLL path and optional invalid-support mode.\n");
        return 2;
    }

    return RunProbe(argv[1], argc == 3);
}
