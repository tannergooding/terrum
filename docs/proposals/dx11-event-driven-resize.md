# DX11 event-driven resize and redraw scenarios

**Status: UNAPPROVED application/evidence recommendations.** These are
source-authored Windows application scenarios, not new language rules or
compiler-recognized loop shapes. The
[sample reference](../../samples/design/win32-dx11-triangle/README.md) owns
current entry points; the [pipeline proposal](dx11-direct-pipeline.md) owns
submission and bounded image evidence.

## Callback-to-queue notification

WM_SIZE reaches WNDPROC, including through DefWindowProcW during
WM_WINDOWPOSCHANGED; it need not appear in PeekMessageW or coincide with paint.
Recommend a callback that posts the same HWND, WM_APP (`0x8000`), unchanged
SIZE_* wParam and zero lParam, then returns LRESULT zero. It posts no pointer
or dimensions and performs no DXGI/D3D work.

The authored PostMessageW import is
`int32(HWND, uint32, nuint, nint)`: BOOL in EAX and arguments in
RCX/EDX/R8/R9. Nonzero BOOL, including negative values, means success.
Do not substitute HRESULT's signed-success rule.

On post failure, remove pending WM_QUIT before posting exit code 1 so a prior
normal close cannot hide the failure. The WM_CLOSE handler leaves an already
pending quit intact instead of overwriting it with PostQuitMessage(0).
These are application failure-precedence choices, not generic callback
lowering rules.

## Drain, coalesce and observe

The creating/render thread drains messages with PeekMessageW using PM_REMOVE.
Only target-HWND WM_APP markers update resizePending and the latest minimized
state; consume these without translation/dispatch. Other windows, thread
markers and unrelated IDs do not affect that state. Ignore WM_SIZE's packed,
truncated dimensions.

Track redrawPending independently for target-window WM_PAINT. Translate and
dispatch paint normally so DefWindowProcW validates its update region.
After draining and dispatching the batch, use one authoritative GetClientRect
snapshot for both ResizeBuffers and the viewport. Reject negative dimensions
or a nonzero client origin; suppress minimized/zero extents. An initial or
changed positive extent requests resize and rendering. At an unchanged
positive extent, paint requests a frame without ResizeBuffers; resize alone
does no work. Coalesced resize and paint produce at most one frame.

Block with GetMessageW when no work remains. Its positive/zero/negative
classification differs from BOOL's nonzero rule. Quit takes precedence over
resize/redraw, is never dispatched, and propagates a nonzero code through
normal finalization rather than reporting successful shutdown. Sustained
message production can defer observation until the queue drains; no artificial
queue cap or synthetic fallback is implied.

## Surface and failure ordering

For each changed positive extent:

1. Clear context state while owners live; retire the RTV before its back buffer.
2. Call authored IDXGISwapChain::ResizeBuffers slot 13 with
   `HRESULT(IDXGISwapChain*,UINT,UINT,UINT,DXGI_FORMAT,UINT)` and arguments
   `0,width,height,DXGI_FORMAT_UNKNOWN,0`. The final two arguments occupy
   Windows x64 caller stack offsets 32 and 40. Signed nonnegative results,
   including positive success codes, are success.
3. Acquire fresh back-buffer/RTV owners, use the same RECT's viewport, rebind
   all pipeline state, draw and Present.

Use lexical per-frame scopes rather than overwriting live unique owners.
Their inner ClearState finalizer and reverse releases finish before the next
resize. An unchanged-extent paint reacquires those surfaces and rebinds/draws
without resizing. Normalize the first positive post-show extent too; an
earlier pre-show acquisition is not the loop's authoritative observation.

Resize, query, message, Present or reacquisition failure exits through cleanup,
never through stale state. Release a failed output only when its authored
provider contract establishes a live owned value. An unspecified possibly
unwritten pointer cannot be read, adopted or released. Retain outer ClearState
before pipeline/context/swap-chain/device and window/class teardown; it does
not establish GPU completion.

Keep WARP and hardware entry points distinct. No implicit driver fallback is
permitted. General animation, persistent surface owners, reentrant dispatch,
device-loss recovery and broader DXGI flags/formats need separately specified
application/library behavior, not special compiler support.

## Required observations

Headless deterministic fixtures should cover coalescing, latest minimized
state, zero/restored/duplicate extents, dimensions above 65535, foreign/thread
messages, quit precedence and the BOOL/HRESULT/GetMessage distinctions.
Exercise exact release/resize/reacquire order, partial/live/unspecified
failure outputs and single-snapshot use. Native ABI coverage must use unrelated
synthetic APIs too.

For opt-in window tests, distinguish a requested paint redraw from actual
natural exposure. Uncovering a window does not guarantee WM_PAINT; a compositor
may retain its content. Explicit invalidation tests the paint path, not natural
exposure delivery. Callback delivery and a self-driven resize test different
paths from injected queue events.

If image readback is enabled, apply the
[pipeline evidence rules](dx11-direct-pipeline.md#image-evidence-recommendation)
before Present for each exercised generation, including padding/address and
exactly-once Unmap checks. A color-domain observation is not exact coverage,
scanout or a screenshot guarantee. Hardware, visible UI and WARP runtime
checks retain their explicit opt-ins and process supervision.

## Specification integration

There is no new normative DX11 type, source seal or queue-proof opcode.
Use generic imports, layouts, function pointers, vtable calls, output state
and ordinary ownership/cleanup. Retire former source-hash/bound-seal and
compiler-synthesized owner/loop implementations rather than extending their
sample-specific cases. Current support and remaining generic compiler gates
belong in [implementation](../implementation.md) and its
[conformance inventory](../implementation/conformance-gaps.md).
