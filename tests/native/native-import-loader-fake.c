#include <stddef.h>

typedef struct HWND__* HWND;
typedef const wchar_t* LPCWSTR;
typedef unsigned int UINT;

__declspec(dllexport) int __stdcall MessageBoxW(HWND owner, LPCWSTR text, LPCWSTR caption, UINT type)
{
    (void)owner;
    (void)text;
    (void)caption;
    (void)type;
    return 99;
}
