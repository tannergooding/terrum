#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <intrin.h>
#include <stdint.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define CHECK(condition) do { if (!(condition)) { \
    fprintf(stderr, "resize queue fixture line %d: %s\n", __LINE__, #condition); ExitProcess(97); } } while (0)

typedef struct Event {
    UINT kind;
    unsigned owner;
    WPARAM parameter;
    LONG width, height;
} Event;

static Event s_events[40];
static unsigned s_count, s_cursor, s_rectangles, s_dispatches, s_translates, s_quit;
static HWND s_window;
static MSG* s_message;
static RECT s_extent;
static BOOL s_drained;
static char s_mode[40], s_report[MAX_PATH];

static int Mode(const char* name) { return strcmp(s_mode, name) == 0; }

static void Report(char event)
{
    DWORD written;
    HANDLE file = CreateFileA(s_report, FILE_APPEND_DATA, FILE_SHARE_READ, NULL,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    CHECK(file != INVALID_HANDLE_VALUE);
    CHECK(WriteFile(file, &event, 1, &written, NULL) && written == 1);
    CHECK(CloseHandle(file));
}

static void Add(UINT kind, unsigned owner, WPARAM parameter, LONG width, LONG height)
{
    CHECK(s_count < 40);
    s_events[s_count++] = (Event){ kind, owner, parameter, width, height };
}

static void Size(WPARAM parameter, LONG width, LONG height)
{
    Add(WM_SIZE, 1, parameter, width, height);
}

static void Batch(void) { Add(UINT_MAX - 1, 0, 0, 0, 0); }

static void BuildEvents(void)
{
    s_extent = (RECT){ 0, 0, 800, 600 };
    if (Mode("coalesced"))
    {
        Size(SIZE_RESTORED, 640, 480);
        Size(SIZE_MINIMIZED, 777, 888);
        Size(SIZE_RESTORED, 800, 600);
        Batch();
        Size(SIZE_RESTORED, 1024, 768);
        Size(SIZE_RESTORED, 2048, 1024);
        Batch();
    }
    else if (Mode("minimized-last") || Mode("minimized-quit"))
    {
        Size(SIZE_RESTORED, 640, 480);
        Size(SIZE_MINIMIZED, 777, 888);
        Batch();
        if (Mode("minimized-last")) { Size(SIZE_RESTORED, 640, 480); Batch(); }
    }
    else if (Mode("zero-width") || Mode("zero-height") || Mode("zero-both"))
    {
        Size(SIZE_RESTORED, Mode("zero-height") ? 800 : 0, Mode("zero-width") ? 600 : 0);
        Batch();
        Size(SIZE_RESTORED, 640, 480);
        Batch();
    }
    else if (Mode("full-width"))
    {
        Size(SIZE_RESTORED, 70000, 80000);
        Batch();
        Size(SIZE_RESTORED, 4464, 80000);
        Batch();
        Size(SIZE_RESTORED, 4464, 14464);
        Batch();
        Size(SIZE_RESTORED, 4464, 14464);
        Batch();
    }
    else if (Mode("unchanged") || Mode("restore-unchanged"))
    {
        Size(SIZE_RESTORED, 800, 600);
        Batch();
        if (Mode("restore-unchanged")) { Size(SIZE_MINIMIZED, 800, 600); Batch(); }
        Size(SIZE_RESTORED, 800, 600);
        Batch();
    }
    else if (Mode("other-window") || Mode("thread-size"))
    {
        Size(SIZE_RESTORED, 800, 600);
        Batch();
        Add(WM_SIZE, Mode("other-window") ? 2 : 0, SIZE_MINIMIZED, 0, 0);
        Batch();
    }
    else if (Mode("unrelated"))
    {
        Add(WM_NULL, 1, 0, 0, 0);
        Batch();
        Add(WM_NULL, 1, 0, 0, 0);
        Batch();
    }
    else if (Mode("peek-quit") || Mode("pending-quit"))
    {
        if (Mode("pending-quit")) { Size(SIZE_RESTORED, 640, 480); }
        Add(WM_QUIT, 0, 0, 0, 0);
        return;
    }
    else
    {
        Size(SIZE_RESTORED, Mode("negative-width") || Mode("negative-width-zero-height") ? -1
            : Mode("zero-width-negative-height") ? 0 : Mode("maximum-dimensions") ? INT_MAX : 800,
            Mode("negative-height") || Mode("zero-width-negative-height") ? -1
            : Mode("negative-width-zero-height") ? 0 : Mode("maximum-dimensions") ? INT_MAX : 600);
        Batch();
    }
    if (Mode("message-error")) { Add(UINT_MAX, 0, 0, 0, 0); }
    else if (Mode("get-quit")) { Add(WM_QUIT, 0, 0, 0, 0); }
    else
    {
        Add(WM_CLOSE, 1, 0, 0, 0);
        Add(WM_QUIT, 0, 0, 0, 0);
    }
}

static void MessageAddress(MSG* message)
{
    CHECK(message && ((uintptr_t)message & 7) == 0);
    if (!s_message) { s_message = message; }
    CHECK(message == s_message && IsWindow(s_window));
}

static BOOL Next(MSG* message)
{
    const Event* event;
    CHECK(s_cursor < s_count);
    event = &s_events[s_cursor++];
    CHECK(event->kind != UINT_MAX - 1);
    memset(message, 0, sizeof(*message));
    message->hwnd = event->owner == 1 ? s_window
        : event->owner == 2 ? (HWND)(uintptr_t)0x1234 : NULL;
    message->message = event->kind;
    message->wParam = event->parameter;
    /* Deliberately wrong/truncated packed dimensions: only the post-drain RECT is authoritative. */
    message->lParam = MAKELPARAM(17, 19);
    if (event->kind == WM_SIZE && event->owner == 1)
    {
        s_extent.right = event->width;
        s_extent.bottom = event->height;
    }
    s_drained = FALSE;
    return TRUE;
}

__declspec(dllexport) HWND WINAPI NativeFfiResizeQueueCreateWindowExW(DWORD exStyle, LPCWSTR className,
    LPCWSTR title, DWORD style, int x, int y, int width, int height,
    HWND parent, HMENU menu, HINSTANCE instance, LPVOID parameter)
{
    CHECK(!s_window && !(style & WS_VISIBLE));
    s_window = CreateWindowExW(exStyle, className, title, style, x, y, width, height,
        parent, menu, instance, parameter);
    CHECK(s_window && !IsWindowVisible(s_window));
    Report('A');
    return s_window;
}

__declspec(dllexport) __declspec(noinline) BOOL WINAPI NativeFfiResizeQueuePeekMessageW(
    LPMSG message, HWND window, UINT minimum, UINT maximum, UINT remove)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    MessageAddress(message);
    CHECK(!window && !minimum && !maximum && remove == PM_REMOVE
        && ((uintptr_t)stack & 15) == 8 && stack[5] == PM_REMOVE);
    CHECK(s_cursor < s_count);
    if (s_events[s_cursor].kind == UINT_MAX - 1)
    {
        ++s_cursor;
        s_drained = TRUE;
        Report('E');
        return FALSE;
    }
    Report('P');
    Next(message);
    return Mode("bool-nonzero") ? -7 : TRUE;
}

__declspec(dllexport) __declspec(noinline) BOOL WINAPI NativeFfiResizeQueueGetMessageW(
    LPMSG message, HWND window, UINT minimum, UINT maximum)
{
    const uintptr_t* stack = (const uintptr_t*)_AddressOfReturnAddress();
    MessageAddress(message);
    CHECK(!window && !minimum && !maximum && s_drained && ((uintptr_t)stack & 15) == 8);
    CHECK(s_cursor < s_count);
    if (s_events[s_cursor].kind == UINT_MAX)
    {
        ++s_cursor;
        Report('!');
        return -1;
    }
    Report('G');
    Next(message);
    if (message->message == WM_QUIT) { return FALSE; }
    return Mode("bool-nonzero") ? INT_MAX : TRUE;
}

__declspec(dllexport) BOOL WINAPI NativeFfiResizeQueueTranslateMessage(const MSG* message)
{
    CHECK(message == s_message && message->message != WM_QUIT && !s_drained);
    ++s_translates;
    Report('T');
    return FALSE;
}

__declspec(dllexport) LRESULT WINAPI NativeFfiResizeQueueDispatchMessageW(const MSG* message)
{
    CHECK(message == s_message && message->message != WM_QUIT && s_translates == s_dispatches + 1);
    ++s_dispatches;
    Report('D');
    if (message->message == WM_CLOSE && message->hwnd == s_window)
    {
        return DispatchMessageW(message);
    }
    return (LRESULT)-19;
}

__declspec(dllexport) BOOL WINAPI NativeFfiResizeQueueGetClientRect(HWND window, LPRECT rectangle)
{
    CHECK(window == s_window && IsWindow(window) && rectangle && s_drained
        && ((uintptr_t)rectangle & 3) == 0
        && ((uintptr_t)rectangle + sizeof(*rectangle) <= (uintptr_t)s_message
            || (uintptr_t)s_message + sizeof(*s_message) <= (uintptr_t)rectangle));
    CHECK(!rectangle->left && !rectangle->top && !rectangle->right && !rectangle->bottom);
    ++s_rectangles;
    Report('R');
    if (Mode("query-error")) { return FALSE; }
    *rectangle = s_extent;
    if (Mode("left")) { rectangle->left = 1; }
    if (Mode("top")) { rectangle->top = 1; }
    return Mode("bool-nonzero") ? -9 : TRUE;
}

__declspec(dllexport) void WINAPI NativeFfiResizeQueuePostQuitMessage(int code)
{
    CHECK(code == 0 && !s_quit++);
    Report('Q');
}

__declspec(dllexport) BOOL WINAPI NativeFfiResizeQueueDestroyWindow(HWND window)
{
    CHECK(window == s_window && !IsWindowVisible(window));
    Report('W');
    if (Mode("destroy-error")) { return FALSE; }
    CHECK(DestroyWindow(window));
    return TRUE;
}

__declspec(dllexport) BOOL WINAPI NativeFfiResizeQueueUnregisterClassW(LPCWSTR name, HINSTANCE instance)
{
    CHECK(!IsWindow(s_window));
    Report('U');
    if (Mode("unregister-error")) { return FALSE; }
    CHECK(UnregisterClassW(name, instance));
    return TRUE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance; (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        CHECK(GetEnvironmentVariableA("TERRUM_DX11_RESIZE_QUEUE_MODE", s_mode, sizeof(s_mode)));
        CHECK(GetEnvironmentVariableA("TERRUM_DX11_RESIZE_QUEUE_REPORT", s_report, sizeof(s_report)));
        BuildEvents();
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        CHECK(Mode("destroy-error") || !IsWindow(s_window));
        CHECK(s_translates == s_dispatches);
        Report('Z');
    }
    return TRUE;
}
