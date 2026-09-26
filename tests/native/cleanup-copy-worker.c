// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>

typedef int (*Check)(void);
typedef int (*CheckTrace)(int);
typedef void (*ResetTrace)(void);

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        return 1;
    }
    HMODULE provider = LoadLibraryA(argv[1]);
    HMODULE target = LoadLibraryA(argv[2]);
    if (!provider || !target)
    {
        fprintf(stderr, "Could not load cleanup ABI fixture: %lu\n", GetLastError());
        return 2;
    }
    CheckTrace trace = (CheckTrace)GetProcAddress(provider, "CheckTrace");
    ResetTrace reset = (ResetTrace)GetProcAddress(provider, "ResetTrace");
    if (!trace || !reset)
    {
        return 3;
    }
    const char* names[] = { "Check", "CheckReplacement", "CheckStaged", "CheckPendingReturn", "CheckNested" };
    const int drops[] = { 1, 2, 2, 2, 1 };
    int result = 0;
    __try
    {
        for (int index = 0; index < 5; index++)
        {
            Check check = (Check)GetProcAddress(target, names[index]);
            if (!check)
            {
                result = 3;
                break;
            }
            reset();
            if (check() != 42 || trace(drops[index]) != 1)
            {
                fprintf(stderr, "Cleanup argument aliasing in %s.\n", names[index]);
                result = 71;
                break;
            }
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        fprintf(stderr, "Unexpected cleanup ABI exception: %08lX\n", GetExceptionCode());
        result = 4;
    }
    if (!FreeLibrary(target) || !FreeLibrary(provider))
    {
        return 5;
    }
    if (result == 0)
    {
        puts("cleanup-copy-abi parent-scratch=clobbered child=42 cases=5");
    }
    return result;
}
