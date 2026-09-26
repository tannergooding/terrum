// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef struct Ticket { int32_t Id; } Ticket;
typedef struct WideTicket { int64_t Signed; uint64_t Unsigned; } WideTicket;
typedef int32_t (*Consume)(Ticket);
typedef Ticket (*Relay)(Ticket);
typedef WideTicket (*RelayWide)(WideTicket);
typedef int32_t (*ConsumeWide)(WideTicket);
typedef WideTicket (*RelayLast)(int32_t, int32_t, int32_t, int32_t, int32_t, WideTicket);
typedef uint8_t (*TryTicket)(uint8_t, Ticket*);
typedef uint8_t (*TryWide)(uint8_t, int32_t, int32_t, int32_t, int32_t, WideTicket*);
typedef uint8_t (*TryPartial)(Ticket*, WideTicket*);
typedef uint8_t (*CatchOutputFailure)(Ticket*);
typedef int32_t (*LaterArgumentFailure)(void);
typedef void* (*RawRelayWide)(void*, WideTicket);

static int Check(HMODULE image)
{
    Consume consume = (Consume)GetProcAddress(image, "Consume");
    Relay relay = (Relay)GetProcAddress(image, "Relay");
    RelayWide relayWide = (RelayWide)GetProcAddress(image, "RelayWide");
    ConsumeWide consumeWide = (ConsumeWide)GetProcAddress(image, "ConsumeWide");
    RelayLast relayLast = (RelayLast)GetProcAddress(image, "RelayLast");
    TryTicket tryTicket = (TryTicket)GetProcAddress(image, "TryTicket");
    TryWide tryWide = (TryWide)GetProcAddress(image, "TryWide");
    TryPartial tryPartial = (TryPartial)GetProcAddress(image, "TryPartial");
    CatchOutputFailure catchOutput = (CatchOutputFailure)GetProcAddress(image, "CatchOutputFailure");
    LaterArgumentFailure laterArgument = (LaterArgumentFailure)GetProcAddress(image, "LaterArgumentFailure");
    RawRelayWide rawRelay = (RawRelayWide)GetProcAddress(image, "RelayWide");
    if (!consume || !relay || !relayWide || !consumeWide || !relayLast
        || !tryTicket || !tryWide || !tryPartial || !catchOutput || !laterArgument || !rawRelay)
    {
        fprintf(stderr, "Missing owned-call ABI export.\n");
        return 10;
    }
    if (sizeof(Ticket) != 4 || sizeof(WideTicket) != 16)
    {
        return 11;
    }

    Ticket ticket = {42};
    Ticket returned = relay(ticket);
    if (consume(returned) != 42)
    {
        return 12;
    }
    WideTicket wide = {INT64_MIN, UINT64_MAX};
    WideTicket wideReturned = relayWide(wide);
    if (consumeWide(wideReturned) != 42)
    {
        return 13;
    }
    WideTicket last = {INT64_MIN, UINT64_MAX};
    WideTicket lastReturned = relayLast(1, 2, 3, 4, 5, last);
    if (consumeWide(lastReturned) != 42)
    {
        return 14;
    }

    // Raw guards are vacant output storage, not additional live Terrum owners.
    __declspec(align(16)) uint8_t guarded[48];
    memset(guarded, 0xA5, sizeof(guarded));
    WideTicket rawInput = {INT64_MIN, UINT64_MAX};
    if (rawRelay(guarded + 16, rawInput) != guarded + 16)
    {
        return 15;
    }
    for (int index = 0; index < 48; index++)
    {
        if ((index < 16 || index >= 32) && guarded[index] != 0xA5)
        {
            return 16;
        }
    }
    WideTicket rawReturned;
    memcpy(&rawReturned, guarded + 16, sizeof(rawReturned));
    if (consumeWide(rawReturned) != 42)
    {
        return 17;
    }

    for (int iteration = 0; iteration < 2; iteration++)
    {
        Ticket smallStorage[3];
        __declspec(align(16)) WideTicket wideStorage[3];
        uint8_t smallBefore[sizeof(smallStorage)];
        uint8_t wideBefore[sizeof(wideStorage)];
        memset(smallStorage, 0xA5, sizeof(smallStorage));
        memset(wideStorage, 0xA5, sizeof(wideStorage));
        memcpy(smallBefore, smallStorage, sizeof(smallBefore));
        memcpy(wideBefore, wideStorage, sizeof(wideBefore));
        if (tryTicket(0, &smallStorage[1]) || tryWide(0, 1, 2, 3, 4, &wideStorage[1])
            || tryPartial(&smallStorage[1], &wideStorage[1]) || catchOutput(&smallStorage[1])
            || memcmp(smallBefore, smallStorage, sizeof(smallBefore)) != 0
            || memcmp(wideBefore, wideStorage, sizeof(wideBefore)) != 0)
        {
            return 18;
        }
        if (!tryTicket(1, &smallStorage[1]) || consume(smallStorage[1]) != 42
            || !tryWide(1, 1, 2, 3, 4, &wideStorage[1]) || consumeWide(wideStorage[1]) != 42)
        {
            return 19;
        }
        if (memcmp(smallBefore, smallStorage, sizeof(Ticket)) != 0
            || memcmp(smallBefore + 2 * sizeof(Ticket), &smallStorage[2], sizeof(Ticket)) != 0
            || memcmp(wideBefore, wideStorage, sizeof(WideTicket)) != 0
            || memcmp(wideBefore + 2 * sizeof(WideTicket), &wideStorage[2], sizeof(WideTicket)) != 0)
        {
            return 20;
        }
        if (laterArgument() != 42)
        {
            return 21;
        }
    }
    return 0;
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        return 1;
    }
    HMODULE image = LoadLibraryA(argv[1]);
    if (!image)
    {
        fprintf(stderr, "Could not load owned-call DLL: %lu\n", GetLastError());
        return 2;
    }
    int result;
    __try
    {
        result = Check(image);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        fprintf(stderr, "Unexpected native exception: %08lX\n", GetExceptionCode());
        result = 3;
    }
    if (!FreeLibrary(image))
    {
        return 4;
    }
    if (result == 0)
    {
        puts("owned-call-abi direct=4 indirect=16 stack=6 publication=checked recovery=42");
    }
    return result;
}
