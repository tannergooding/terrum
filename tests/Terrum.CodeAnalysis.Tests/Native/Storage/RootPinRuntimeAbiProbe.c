#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>

#include "RootPinLoadedTypesV2.h"

typedef const RootPinRuntimeInfoV1* (ROOTPIN_LOADED_TYPES_CALL *GetRuntimeInfoFunction)(void);
typedef uint32_t (ROOTPIN_LOADED_TYPES_CALL *AdmitLoadedTypesFunction)(
    RootPinDomainV1,
    const RootPinTypeBatchV2*,
    RootPinLoadedTypeV1**,
    uint32_t);
typedef RootPinAdmitLoadedMethodsEntryV1 AdmitLoadedMethodsFunction;

typedef char AssertPointerSize[sizeof(void*) == 8 ? 1 : -1];
typedef char AssertRuntimeFunctionTableSize[
    sizeof(RootPinRuntimeFunctionTableV1) == 104 ? 1 : -1];
typedef char AssertRuntimeInfoSize[
    sizeof(RootPinRuntimeInfoV1) == 112 ? 1 : -1];
typedef char AssertRuntimeFunctionTableOffset[
    offsetof(RootPinRuntimeInfoV1, functionTable) == 8 ? 1 : -1];
typedef char AssertRuntimeRootExactOffset[
    offsetof(RootPinRuntimeFunctionTableV1, rootExact) == 8 ? 1 : -1];
typedef char AssertRuntimeFinishCallOffset[
    offsetof(RootPinRuntimeFunctionTableV1, finishCall) == 96 ? 1 : -1];

#define CHECK(expression) \
    do { \
        if (!(expression)) { \
            fprintf(stderr, "RootPin runtime ABI probe failed at line %d.\n", __LINE__); \
            return 1; \
        } \
    } while (0)

int main(int argc, char** argv)
{
    HMODULE library;
    FARPROC procedure;
    GetRuntimeInfoFunction getRuntimeInfo = NULL;
    AdmitLoadedTypesFunction admitTypes = NULL;
    AdmitLoadedMethodsFunction admitMethods = NULL;
    const RootPinRuntimeInfoV1* runtimeInfo;
    RootPinMethodBatchV1 methodBatch = { 0 };
    RootPinLoadedMethodV1 methodOutput = NULL;

    CHECK(argc == 2);
    library = LoadLibraryA(argv[1]);
    CHECK(library != NULL);

    procedure = GetProcAddress(library, "TerrumRootPinGetRuntimeV1");
    CHECK(procedure != NULL && sizeof(getRuntimeInfo) == sizeof(procedure));
    memcpy(&getRuntimeInfo, &procedure, sizeof(getRuntimeInfo));
    runtimeInfo = getRuntimeInfo();
    CHECK(runtimeInfo != NULL && runtimeInfo->domain != NULL
        && runtimeInfo->functionTable.abiRevision == 1
        && runtimeInfo->functionTable.byteSize == 104);
    CHECK(runtimeInfo->functionTable.rootExact != NULL
        && runtimeInfo->functionTable.pinExact != NULL
        && runtimeInfo->functionTable.pinObject != NULL
        && runtimeInfo->functionTable.resolveExact != NULL
        && runtimeInfo->functionTable.retainSupport != NULL
        && runtimeInfo->functionTable.releaseSupport != NULL
        && runtimeInfo->functionTable.enterFrame != NULL
        && runtimeInfo->functionTable.exitFrame != NULL
        && runtimeInfo->functionTable.poll != NULL
        && runtimeInfo->functionTable.beginCall != NULL
        && runtimeInfo->functionTable.returnSupport != NULL
        && runtimeInfo->functionTable.finishCall != NULL);

    procedure = GetProcAddress(library, "TerrumRootPinAdmitLoadedTypesV2");
    CHECK(procedure != NULL && sizeof(admitTypes) == sizeof(procedure));
    memcpy(&admitTypes, &procedure, sizeof(admitTypes));
    procedure = GetProcAddress(library, "TerrumRootPinAdmitLoadedMethodsV1");
    CHECK(procedure != NULL && sizeof(admitMethods) == sizeof(procedure));
    memcpy(&admitMethods, &procedure, sizeof(admitMethods));

    methodBatch.abiRevision = 1;
    methodBatch.byteSize = sizeof(methodBatch);
    methodBatch.targetProfileId = 3;
    methodBatch.targetProfileRevision = 1;
    methodBatch.methodCount = 1;
    CHECK(admitMethods(runtimeInfo->domain, &methodBatch, &methodOutput, 1)
        == ROOTPIN_TYPE_STATUS_UNSUPPORTED && methodOutput == NULL);

    CHECK(FreeLibrary(library) != 0);
    puts("rootpin-runtime-abi=pass");
    return 0;
}
