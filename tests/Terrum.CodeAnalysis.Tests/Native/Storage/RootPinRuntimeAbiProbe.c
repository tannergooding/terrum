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

static int IsModuleRange(
    HMODULE library,
    const void* address,
    SIZE_T length,
    int requireExecutable)
{
    MEMORY_BASIC_INFORMATION information;
    uintptr_t start;
    uintptr_t base;
    uintptr_t offset;
    DWORD protection;
    int executable;
    int readable;

    if (address == NULL || VirtualQuery(address, &information,
            sizeof(information)) != sizeof(information)
        || information.AllocationBase != library
        || information.State != MEM_COMMIT
        || (information.Protect & PAGE_GUARD) != 0
        || (uintptr_t)address < (uintptr_t)information.BaseAddress)
    {
        return 0;
    }

    start = (uintptr_t)address;
    base = (uintptr_t)information.BaseAddress;
    offset = start - base;
    protection = information.Protect & 0xFF;
    executable = protection == PAGE_EXECUTE
        || protection == PAGE_EXECUTE_READ
        || protection == PAGE_EXECUTE_READWRITE
        || protection == PAGE_EXECUTE_WRITECOPY;
    readable = protection == PAGE_READONLY
        || protection == PAGE_READWRITE
        || protection == PAGE_WRITECOPY
        || protection == PAGE_EXECUTE_READ
        || protection == PAGE_EXECUTE_READWRITE
        || protection == PAGE_EXECUTE_WRITECOPY;

    return offset <= information.RegionSize
        && length <= information.RegionSize - offset
        && (requireExecutable ? executable : readable);
}

static int IsFunctionInModule(
    HMODULE library,
    const void* functionStorage,
    size_t functionSize)
{
    void* address = NULL;
    if (functionStorage == NULL || functionSize != sizeof(address))
    {
        return 0;
    }

    memcpy(&address, functionStorage, sizeof(address));
    return IsModuleRange(library, address, 1, 1);
}

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
    CHECK(IsFunctionInModule(library, &getRuntimeInfo, sizeof(getRuntimeInfo)));
    runtimeInfo = getRuntimeInfo();
    CHECK(IsModuleRange(library, runtimeInfo, sizeof(*runtimeInfo), 0));
    CHECK(runtimeInfo->domain != NULL
        && IsModuleRange(library, runtimeInfo->domain, 1, 0)
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
    CHECK(IsFunctionInModule(library, &runtimeInfo->functionTable.rootExact,
            sizeof(runtimeInfo->functionTable.rootExact))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.pinExact,
            sizeof(runtimeInfo->functionTable.pinExact))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.pinObject,
            sizeof(runtimeInfo->functionTable.pinObject))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.resolveExact,
            sizeof(runtimeInfo->functionTable.resolveExact))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.retainSupport,
            sizeof(runtimeInfo->functionTable.retainSupport))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.releaseSupport,
            sizeof(runtimeInfo->functionTable.releaseSupport))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.enterFrame,
            sizeof(runtimeInfo->functionTable.enterFrame))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.exitFrame,
            sizeof(runtimeInfo->functionTable.exitFrame))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.poll,
            sizeof(runtimeInfo->functionTable.poll))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.beginCall,
            sizeof(runtimeInfo->functionTable.beginCall))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.returnSupport,
            sizeof(runtimeInfo->functionTable.returnSupport))
        && IsFunctionInModule(library, &runtimeInfo->functionTable.finishCall,
            sizeof(runtimeInfo->functionTable.finishCall)));

    procedure = GetProcAddress(library, "TerrumRootPinAdmitLoadedTypesV2");
    CHECK(procedure != NULL && sizeof(admitTypes) == sizeof(procedure));
    memcpy(&admitTypes, &procedure, sizeof(admitTypes));
    CHECK(IsFunctionInModule(library, &admitTypes, sizeof(admitTypes)));
    procedure = GetProcAddress(library, "TerrumRootPinAdmitLoadedMethodsV1");
    CHECK(procedure != NULL && sizeof(admitMethods) == sizeof(procedure));
    memcpy(&admitMethods, &procedure, sizeof(admitMethods));
    CHECK(IsFunctionInModule(library, &admitMethods, sizeof(admitMethods)));

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
