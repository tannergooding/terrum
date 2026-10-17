#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>

typedef char window_pointer_size[(sizeof(void*) == 8) ? 1 : -1];
typedef char window_result_size[(sizeof(LRESULT) == 8) ? 1 : -1];
typedef char window_message_size[(sizeof(UINT) == 4) ? 1 : -1];
typedef char window_unsigned_parameter_size[(sizeof(WPARAM) == 8) ? 1 : -1];
typedef char window_signed_parameter_size[(sizeof(LPARAM) == 8) ? 1 : -1];

enum
{
    WindowCreating = 1,
    WindowPainting,
    WindowDestroying,
    WindowDestroyed,
    WindowUnregistered
};

typedef struct WindowLifetimeState
{
    DWORD thread;
    ATOM classAtom;
    HWND window;
    int phase;
    int error;
    unsigned int observed;
    unsigned int destroyCalls;
    unsigned int unregisterCalls;
} WindowLifetimeState;

/* Only fixture-owned stack data is exposed to the thread hook, for this invocation. */
static __declspec(thread) WindowLifetimeState* activeWindowLifetime;

static void RecordWindowError(WindowLifetimeState* state, int error)
{
    if (state->error == 0)
    {
        state->error = error;
    }
}

static __declspec(noreturn) void WindowCleanupFailure(void)
{
    /* Returning with a registered caller WNDPROC would violate the no-escape contract. */
    RaiseFailFastException(NULL, NULL, 0);
    ExitProcess(255);
}

static LRESULT CALLBACK ObserveWindowResult(int code, WPARAM wParam, LPARAM lParam)
{
    if (code >= 0 && activeWindowLifetime != NULL)
    {
        WindowLifetimeState* state = activeWindowLifetime;
        const CWPRETSTRUCT* message = (const CWPRETSTRUCT*)lParam;
        /* Window creation may also create unrelated system windows on this thread. */
        if (state->classAtom == 0 || GetClassLongPtrW(message->hwnd, GCW_ATOM) != state->classAtom)
        {
            return CallNextHookEx(NULL, code, wParam, lParam);
        }
        unsigned int ordinal = 0;
        int phase = 0;
        LRESULT expected = 0;
        switch (message->message)
        {
            case WM_NCCREATE:
                ordinal = 1;
                phase = WindowCreating;
                expected = 1;
                if (state->window == NULL)
                {
                    state->window = message->hwnd;
                }
                break;
            case WM_CREATE:
                ordinal = 2;
                phase = WindowCreating;
                break;
            case WM_PAINT:
                ordinal = 3;
                phase = WindowPainting;
                expected = (LRESULT)(INT64_C(0x100000000) + state->thread);
                break;
            case WM_DESTROY:
                ordinal = 4;
                phase = WindowDestroying;
                expected = (LRESULT)(INT64_C(0x200000000) + state->thread);
                break;
            case WM_NCDESTROY:
                ordinal = 5;
                phase = WindowDestroying;
                expected = (LRESULT)(INT64_C(0x300000000) + state->thread);
                break;
            default:
                break;
        }
        if (ordinal != 0)
        {
            if (GetCurrentThreadId() != state->thread || state->phase != phase
                || state->window != message->hwnd || state->observed + 1 != ordinal)
            {
                RecordWindowError(state, 20 + (int)ordinal);
            }
            if (message->lResult != expected)
            {
                RecordWindowError(state, 30 + (int)ordinal);
            }
            if (message->wParam != 0
                || (ordinal <= 2 ? message->lParam == 0 : message->lParam != 0))
            {
                RecordWindowError(state, 40 + (int)ordinal);
            }
            state->observed++;
        }
    }
    return CallNextHookEx(NULL, code, wParam, lParam);
}

__declspec(dllexport) int32_t __cdecl NativeFfiWindowLifetime(WNDPROC callback)
{
    WindowLifetimeState state = { 0 };
    WNDCLASSEXW windowClass = { 0 };
    WNDCLASSEXW remainingClass = { 0 };
    HINSTANCE instance = NULL;
    HHOOK hook = NULL;
    HWND window = NULL;
    wchar_t className[80];
    ATOM registered = 0;
    if (callback == NULL || activeWindowLifetime != NULL)
    {
        return 1;
    }
    state.thread = GetCurrentThreadId();
    state.phase = WindowCreating;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS
        | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (LPCWSTR)&NativeFfiWindowLifetime, &instance)
        || swprintf_s(className, sizeof(className) / sizeof(className[0]), L"Terrum.NativeFfi.WindowLifetime.%lu",
            state.thread) < 0)
    {
        return 2;
    }
    windowClass.cbSize = sizeof(windowClass);
    windowClass.lpfnWndProc = callback;
    windowClass.hInstance = instance;
    windowClass.lpszClassName = className;
    activeWindowLifetime = &state;
    hook = SetWindowsHookExW(WH_CALLWNDPROCRET, ObserveWindowResult, NULL, state.thread);
    if (hook == NULL)
    {
        RecordWindowError(&state, 3);
        goto cleanup;
    }
    registered = RegisterClassExW(&windowClass);
    if (registered == 0)
    {
        RecordWindowError(&state, 4);
        goto cleanup;
    }
    state.classAtom = registered;
    window = CreateWindowExW(0, className, L"", WS_POPUP, 0, 0, 1, 1,
        NULL, NULL, instance, NULL);
    if (window == NULL)
    {
        RecordWindowError(&state, 5);
        goto cleanup;
    }
    if (window != state.window || GetWindowThreadProcessId(window, NULL) != state.thread
        || IsWindowVisible(window) || state.observed != 2)
    {
        RecordWindowError(&state, 6);
    }
    state.phase = WindowPainting;
    if (SendMessageW(window, WM_PAINT, 0, 0) != (LRESULT)(INT64_C(0x100000000) + state.thread)
        || state.observed != 3)
    {
        RecordWindowError(&state, 7);
    }

cleanup:
    if (window != NULL)
    {
        state.phase = WindowDestroying;
        state.destroyCalls++;
        if (GetCurrentThreadId() != state.thread || !DestroyWindow(window) || IsWindow(window))
        {
            WindowCleanupFailure();
        }
        if (state.observed != 5)
        {
            RecordWindowError(&state, 8);
        }
        state.phase = WindowDestroyed;
    }
    if (registered != 0)
    {
        if (window != NULL && state.phase != WindowDestroyed)
        {
            WindowCleanupFailure();
        }
        state.unregisterCalls++;
        if (GetCurrentThreadId() != state.thread || !UnregisterClassW(className, instance))
        {
            WindowCleanupFailure();
        }
        state.phase = WindowUnregistered;
        remainingClass.cbSize = sizeof(remainingClass);
        SetLastError(0);
        if (GetClassInfoExW(instance, className, &remainingClass)
            || GetLastError() != ERROR_CLASS_DOES_NOT_EXIST)
        {
            WindowCleanupFailure();
        }
    }
    if (hook != NULL && !UnhookWindowsHookEx(hook))
    {
        WindowCleanupFailure();
    }
    activeWindowLifetime = NULL;
    windowClass.lpfnWndProc = NULL;
    callback = NULL;
    if (state.error == 0 && (state.phase != WindowUnregistered || state.observed != 5
        || state.destroyCalls != 1 || state.unregisterCalls != 1))
    {
        RecordWindowError(&state, 9);
    }
    return state.error;
}

#ifdef NATIVE_FFI_WINDOW_LIFETIME_SELF_TEST
static LRESULT CALLBACK TestWindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    if (window == NULL || wParam != 0)
    {
        return -1;
    }
    switch (message)
    {
        case WM_NCCREATE:
            return lParam != 0 ? 1 : -1;
        case WM_CREATE:
            return lParam != 0 ? 0 : -1;
        case WM_PAINT:
            return lParam == 0 ? (LRESULT)(INT64_C(0x100000000) + GetCurrentThreadId()) : -1;
        case WM_DESTROY:
            return lParam == 0 ? (LRESULT)(INT64_C(0x200000000) + GetCurrentThreadId()) : -1;
        case WM_NCDESTROY:
            return lParam == 0 ? (LRESULT)(INT64_C(0x300000000) + GetCurrentThreadId()) : -1;
        default:
            return 0;
    }
}

static LRESULT CALLBACK RejectCreation(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    return message == WM_NCCREATE ? 0 : TestWindowProcedure(window, message, wParam, lParam);
}

static LRESULT CALLBACK WrongPaint(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    return message == WM_PAINT ? 0 : TestWindowProcedure(window, message, wParam, lParam);
}

static LRESULT CALLBACK WrongCreate(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    return message == WM_CREATE ? 1 : TestWindowProcedure(window, message, wParam, lParam);
}

static LRESULT CALLBACK OutOfOrderPaint(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    if (message == WM_CREATE)
    {
        SendMessageW(window, WM_PAINT, 0, 0);
    }
    return TestWindowProcedure(window, message, wParam, lParam);
}

static LRESULT CALLBACK WrongDestroy(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    return message == WM_DESTROY ? 0 : TestWindowProcedure(window, message, wParam, lParam);
}

static LRESULT CALLBACK WrongFinalDestroy(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    return message == WM_NCDESTROY ? 0 : TestWindowProcedure(window, message, wParam, lParam);
}

static LRESULT CALLBACK WrongThread(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    const LRESULT result = TestWindowProcedure(window, message, wParam, lParam);
    return message == WM_PAINT ? result + 1 : result;
}

static DWORD WINAPI TestCreatingThread(void* parameter)
{
    (void)parameter;
    return (DWORD)NativeFfiWindowLifetime(TestWindowProcedure);
}

int main(void)
{
    static const struct
    {
        WNDPROC callback;
        int32_t expected;
    } cases[] = {
        { NULL, 1 },
        { TestWindowProcedure, 0 },
        { RejectCreation, 31 },
        { WrongCreate, 32 },
        { OutOfOrderPaint, 23 },
        { WrongPaint, 33 },
        { WrongDestroy, 34 },
        { WrongFinalDestroy, 35 },
        { WrongThread, 33 },
        { TestWindowProcedure, 0 }
    };
    HANDLE thread;
    DWORD result;
    for (size_t index = 0; index < sizeof(cases) / sizeof(cases[0]); index++)
    {
        const int32_t actual = NativeFfiWindowLifetime(cases[index].callback);
        if (actual != cases[index].expected)
        {
            printf("window-lifetime C case %zu: expected %d, actual %d\n",
                index, cases[index].expected, actual);
            return 1;
        }
    }
    thread = CreateThread(NULL, 0, TestCreatingThread, NULL, 0, NULL);
    if (thread == NULL)
    {
        return 2;
    }
    if (WaitForSingleObject(thread, 10000) != WAIT_OBJECT_0
        || !GetExitCodeThread(thread, &result) || !CloseHandle(thread) || result != 0)
    {
        return 3;
    }
    puts("window-lifetime C self-test: 11 cases passed");
    return 0;
}
#endif
