#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>

typedef double (__cdecl *sum_function)(double, int32_t, double, int32_t, double);

int main(int argc, char **argv)
{
    if (argc != 2)
    {
        return 1;
    }

    HMODULE library = LoadLibraryA(argv[1]);
    if (library == NULL)
    {
        return 2;
    }

    FARPROC exported = GetProcAddress(library, "Sum");
    if (exported == NULL || sizeof(exported) != sizeof(sum_function))
    {
        FreeLibrary(library);
        return 3;
    }

    sum_function sum = NULL;
    memcpy(&sum, &exported, sizeof(sum));
    double result = sum(1.5, 0, 3.0, 0, 5.0);
    FreeLibrary(library);
    return result == 9.5 ? 0 : 4;
}
