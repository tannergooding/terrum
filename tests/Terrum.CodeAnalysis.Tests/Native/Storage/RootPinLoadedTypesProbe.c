#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "RootPinLoadedTypesV1.h"

typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *AdmitFunction)(
    RootPinDomainV1,
    const RootPinTypeBatchV1*,
    RootPinLoadedTypeV1**,
    uint32_t);
typedef RootPinDomainV1 (ROOTPIN_LOADED_TYPES_CALL *GetDomainFunction)(void);
typedef void (ROOTPIN_LOADED_TYPES_CALL *SetFailureFunction)(uint32_t);
typedef void (ROOTPIN_LOADED_TYPES_CALL *RetireFunction)(RootPinDomainV1, void*);
typedef void (ROOTPIN_LOADED_TYPES_CALL *OriginCallback)(void*);

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
    RootPinTypeNodeV1* node,
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

static void BuildValidBatch(
    RootPinTypeBatchV1* batch,
    RootPinTypeNodeV1* nodes,
    RootPinTypeFieldV1* fields,
    RootPinTypeOriginV1* origins,
    uint32_t* originUses,
    uint32_t* originCounts)
{
    memset(nodes, 0, sizeof(RootPinTypeNodeV1) * 13);
    memset(fields, 0, sizeof(RootPinTypeFieldV1) * 6);
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
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 8, 8,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 2, 1, 3, 3);
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
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 3, 2, 15, 1);
    SetNode(&nodes[12], 12001, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
        (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 5, 1,
        (uint32_t)ROOTPIN_TYPE_NO_INDEX, 5, 1, 16, 1);

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
    fields[2].knownAlignment = 8;
    fields[2].valueNode = 2;
    fields[3].fieldKey = 301;
    fields[3].offset = 0;
    fields[3].knownAlignment = 4;
    fields[3].valueNode = 0;
    fields[4].fieldKey = 302;
    fields[4].offset = 0;
    fields[4].knownAlignment = 4;
    fields[4].valueNode = 0;
    fields[5].fieldKey = 401;
    fields[5].offset = 1;
    fields[5].knownAlignment = 1;
    fields[5].valueNode = 0;

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

    batch->abiRevision = 1;
    batch->byteSize = sizeof(*batch);
    batch->targetProfileId = 2;
    batch->targetProfileRevision = 1;
    batch->nodeCount = 13;
    batch->fieldCount = 6;
    batch->originCount = 3;
    batch->originUseCount = 17;
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

static int RunProbe(const char* libraryPath)
{
    HMODULE library = LoadLibraryA(libraryPath);
    FARPROC procedure;
    AdmitFunction admit = NULL;
    GetDomainFunction getDomain = NULL;
    SetFailureFunction setFailure = NULL;
    RootPinDomainV1 domain;
    RootPinTypeBatchV1 batch;
    RootPinTypeNodeV1 nodes[13];
    RootPinTypeFieldV1 fields[6];
    RootPinTypeOriginV1 origins[3];
    uint32_t originUses[17];
    uint32_t originCounts[3] = { 0, 0, 0 };
    RootPinLoadedTypeV1* outputs[13];
    RootPinLoadedTypeV1* repeated[13];
    uint32_t status;
    uint32_t index;

    CHECK(library != NULL);

    procedure = GetProcAddress(library, "TerrumRootPinAdmitLoadedTypesV1");
    CHECK(procedure != NULL && sizeof(admit) == sizeof(procedure));
    memcpy(&admit, &procedure, sizeof(admit));
    procedure = GetProcAddress(library, "RootPinTestGetDomainV1");
    CHECK(procedure != NULL && sizeof(getDomain) == sizeof(procedure));
    memcpy(&getDomain, &procedure, sizeof(getDomain));
    procedure = GetProcAddress(library, "RootPinTestFailAdmissionAtStageV1");
    CHECK(procedure != NULL && sizeof(setFailure) == sizeof(procedure));
    memcpy(&setFailure, &procedure, sizeof(setFailure));
    CHECK(admit != NULL && getDomain != NULL && setFailure != NULL);

    domain = getDomain();
    CHECK(domain != NULL);
    BuildValidBatch(&batch, nodes, fields, origins, originUses, originCounts);
    memset(outputs, 0xA5, sizeof(outputs));
    status = admit(domain, &batch, outputs, 13);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS);
    CHECK(outputs[0] != NULL && outputs[1] != NULL && outputs[2] != NULL
        && outputs[3] != NULL && outputs[4] != NULL && outputs[5] != NULL
        && outputs[6] != NULL && outputs[7] != NULL && outputs[8] != NULL
        && outputs[9] != NULL && outputs[10] != NULL && outputs[11] != NULL
        && outputs[12] != NULL);
    CHECK(outputs[0] == outputs[7]);
    CHECK(outputs[0] != outputs[1] && outputs[5] != outputs[6]);
    CHECK(outputs[8] != outputs[3] && outputs[9] != outputs[0]
        && outputs[10] != outputs[9] && outputs[11] != outputs[1]
        && outputs[12] != outputs[1]);
    CHECK(originCounts[0] == 1 && originCounts[1] == 1 && originCounts[2] == 1);

    status = admit(domain, &batch, repeated, 13);
    CHECK(status == ROOTPIN_TYPE_STATUS_SUCCESS);
    for (index = 0; index < 13; index++)
    {
        CHECK(repeated[index] == outputs[index]);
    }
    CHECK(originCounts[0] == 1 && originCounts[1] == 1 && originCounts[2] == 1);

    nodes[7].size = 8;
    nodes[7].alignment = 8;
    memset(repeated, 0xA5, sizeof(repeated));
    status = admit(domain, &batch, repeated, 13);
    CHECK(status == ROOTPIN_TYPE_STATUS_INVALID && OutputsAreZero(repeated, 13));
    CHECK(originCounts[0] == 1 && originCounts[1] == 1 && originCounts[2] == 1);
    nodes[7].size = 4;
    nodes[7].alignment = 4;

    nodes[5].child = 99;
    memset(repeated, 0xA5, sizeof(repeated));
    status = admit(domain, &batch, repeated, 13);
    CHECK(status == ROOTPIN_TYPE_STATUS_INVALID && OutputsAreZero(repeated, 13));
    nodes[5].child = 0;

    batch.targetProfileRevision = 2;
    memset(repeated, 0xA5, sizeof(repeated));
    status = admit(domain, &batch, repeated, 13);
    CHECK(status == ROOTPIN_TYPE_STATUS_UNSUPPORTED && OutputsAreZero(repeated, 13));
    batch.targetProfileRevision = 1;

    {
        RootPinTypeNodeV1 cycleNode = { 0 };
        RootPinTypeFieldV1 cycleField = { 0 };
        uint32_t cycleOriginUse = 0;
        RootPinTypeBatchV1 cycleBatch = { 0 };
        RootPinLoadedTypeV1* cycleOutput[1];

        SetNode(&cycleNode, 12000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
            (uint32_t)ROOTPIN_TYPE_SHAPE_FIELDS, 4, 4,
            (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 1, 0, 1);
        cycleNode.layoutKind = ROOTPIN_TYPE_LAYOUT_SEQUENTIAL;
        cycleField.fieldKey = 401;
        cycleField.offset = 0;
        cycleField.knownAlignment = 4;
        cycleField.valueNode = 0;
        cycleBatch.abiRevision = 1;
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
        RootPinTypeNodeV1 payloadNodes[2] = { 0 };
        RootPinTypeOriginV1 payloadOrigins[2] = { origins[0], origins[1] };
        uint32_t payloadUses[3] = { 0, 0, 1 };
        RootPinTypeBatchV1 payloadBatch = { 0 };
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
        payloadBatch.abiRevision = 1;
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
        RootPinTypeNodeV1 extraNode = { 0 };
        RootPinTypeOriginV1 extraOrigin = { 0 };
        uint32_t extraUse = 0;
        RootPinTypeBatchV1 extraBatch = { 0 };
        RootPinLoadedTypeV1* extraOutput[1];

        SetNode(&extraNode, 9000, (uint32_t)ROOTPIN_TYPE_ROLE_VALUE,
            (uint32_t)ROOTPIN_TYPE_SHAPE_LEAF,
            4, 4, (uint32_t)ROOTPIN_TYPE_NO_INDEX, 0, 0, 0, 1);
        SetOrigin(&extraOrigin, 90, (uint64_t)(uintptr_t)&originCounts[0],
            (uint32_t)ROOTPIN_TYPE_ORIGIN_SEMANTIC_LAYOUT, RetainOrigin, ReleaseOrigin);
        extraBatch.abiRevision = 1;
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
