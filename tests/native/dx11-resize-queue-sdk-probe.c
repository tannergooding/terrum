#include "win32-window-sdk-probe.c"
#include <dxgi.h>

ASSERT_SIZE(resize_queue_message_size, MSG, 48);
ASSERT_SIZE(resize_queue_rectangle_size, RECT, 16);
typedef char resize_queue_message_hwnd[(offsetof(MSG, hwnd) == 0) ? 1 : -1];
typedef char resize_queue_message_kind[(offsetof(MSG, message) == 8) ? 1 : -1];
typedef char resize_queue_message_wparam[(offsetof(MSG, wParam) == 16) ? 1 : -1];
typedef char resize_queue_message_lparam[(offsetof(MSG, lParam) == 24) ? 1 : -1];
typedef char resize_queue_rect_right[(offsetof(RECT, right) == 8) ? 1 : -1];
typedef char resize_queue_rect_bottom[(offsetof(RECT, bottom) == 12) ? 1 : -1];
typedef char resize_queue_remove[(PM_REMOVE == 1) ? 1 : -1];
typedef char resize_queue_size[(WM_SIZE == 5 && SIZE_MINIMIZED == 1 && WM_QUIT == 18) ? 1 : -1];
typedef char resize_signal_private_message[(WM_APP == 0x8000 && WM_APP > WM_USER) ? 1 : -1];
ASSERT_SIZE(resize_signal_wparam_size, WPARAM, 8);
ASSERT_SIZE(resize_signal_lparam_size, LPARAM, 8);
typedef char resize_queue_buffers_slot[(offsetof(IDXGISwapChainVtbl, ResizeBuffers) == 104) ? 1 : -1];

void NativeFfiResizeQueueSdkSignatures(const IDXGISwapChainVtbl* swap)
{
    BOOL (WINAPI *peek)(LPMSG, HWND, UINT, UINT, UINT) = PeekMessageW;
    BOOL (WINAPI *get)(LPMSG, HWND, UINT, UINT) = GetMessageW;
    BOOL (WINAPI *translate)(const MSG*) = TranslateMessage;
    LRESULT (WINAPI *dispatch)(const MSG*) = DispatchMessageW;
    BOOL (WINAPI *rectangle)(HWND, LPRECT) = GetClientRect;
    BOOL (WINAPI *post)(HWND, UINT, WPARAM, LPARAM) = PostMessageW;
    HRESULT (STDMETHODCALLTYPE *resize)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT)
        = swap->ResizeBuffers;
    (void)peek; (void)get; (void)translate; (void)dispatch; (void)rectangle; (void)post; (void)resize;
}
