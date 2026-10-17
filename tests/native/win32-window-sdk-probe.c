#include <stddef.h>
#include <windows.h>

#if !defined(_WIN64) || !defined(_M_X64)
#error This probe requires the Windows x64 ABI.
#endif

#define ASSERT_SIZE(name, type, expected) \
    typedef char name[(sizeof(type) == (expected)) ? 1 : -1]

ASSERT_SIZE(hwnd_width, HWND, 8);
ASSERT_SIZE(hinstance_width, HINSTANCE, 8);
ASSERT_SIZE(hmenu_width, HMENU, 8);
ASSERT_SIZE(lpvoid_width, LPVOID, 8);
ASSERT_SIZE(wndclassex_width, WNDCLASSEXW, 80);
ASSERT_SIZE(create_window_style_width, DWORD, 4);
ASSERT_SIZE(create_window_coordinate_width, int, 4);
ASSERT_SIZE(destroy_window_result_width, BOOL, 4);
typedef char destroy_window_result_is_signed[((BOOL)-1 < 0) ? 1 : -1];
typedef char wndclassex_callback_offset_is_eight[(offsetof(WNDCLASSEXW, lpfnWndProc) == 8) ? 1 : -1];
typedef char wndclassex_instance_offset_is_twenty_four[(offsetof(WNDCLASSEXW, hInstance) == 24) ? 1 : -1];
typedef char wndclassex_class_name_offset_is_sixty_four[(offsetof(WNDCLASSEXW, lpszClassName) == 64) ? 1 : -1];
ASSERT_SIZE(atom_width, ATOM, 2);

void NativeFfiCheckWin32WindowSignatures(void)
{
    HWND (WINAPI *createWindow)(DWORD, LPCWSTR, LPCWSTR, DWORD, int, int, int, int,
        HWND, HMENU, HINSTANCE, LPVOID) = CreateWindowExW;
    BOOL (WINAPI *destroyWindow)(HWND) = DestroyWindow;
    HMODULE (WINAPI *getModuleHandle)(LPCWSTR) = GetModuleHandleW;
    ATOM (WINAPI *registerClass)(const WNDCLASSEXW *) = RegisterClassExW;
    BOOL (WINAPI *unregisterClass)(LPCWSTR, HINSTANCE) = UnregisterClassW;
    (void)createWindow;
    (void)destroyWindow;
    (void)getModuleHandle;
    (void)registerClass;
    (void)unregisterClass;
}
