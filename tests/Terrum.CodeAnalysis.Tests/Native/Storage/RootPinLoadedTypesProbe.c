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

static void RetirePayload(RootPinDomainV1 domain, void* payload)
{
    (void)domain;
    (void)payload;
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

#undef LOAD_FUNCTION

static int RunProbe(const char* libraryPath)
{
    HMODULE library = LoadLibraryA(libraryPath);
    FARPROC procedure;
    AdmitFunction admit = NULL;
    GetDomainFunction getDomain = NULL;
    SetFailureFunction setFailure = NULL;
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
    CHECK(admit != NULL && getDomain != NULL && setFailure != NULL);

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
    puts("rootpin-loaded-types=pass");
    return 0;
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        fprintf(stderr, "Expected the provider DLL path.\n");
        return 2;
    }

    return RunProbe(argv[1]);
}
