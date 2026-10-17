# DX11 sample event-driven resizing

**Status: UNAPPROVED recommendation.** The proposed sample-specific Windows x64 gate
does not widen the fixed-size WARP/hardware pipeline or `windows-native-ffi`
contracts, change canonical grammar, or establish general DXGI/COM support.

Recommend a source-authored callback-to-queue handoff.
[`WM_SIZE`](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-size)
is sent to WNDPROC, including by DefWindowProcW while processing
WM_WINDOWPOSCHANGED; the renderer must not depend on finding it in PeekMessageW
or on a coincident WM_PAINT. Handle WM_SIZE in the Terrum callback by calling
the catalogued System32
[`PostMessageW`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew)
with the same HWND, `WM_APP` (`0x8000`, the first application-private message),
the unchanged `SIZE_*` wParam and zero lParam, then return LRESULT zero.
The exact x64 import is `int32(HWND, uint32, nuint, nint)`:
BOOL in EAX, HWND in RCX, UINT in EDX, eight-byte WPARAM in R8 and LPARAM in R9.
It queues no pointer or dimensions and performs no DXGI/D3D work.

On the creating/render thread, drain `MSG` events with
`PeekMessageW(NULL, 0, 0, PM_REMOVE)`, consume only target-HWND WM_APP markers
without translation/dispatch, and record local `resizePending` and the latest
`SIZE_MINIMIZED` state. Query GetClientRect only after draining and dispatching
the batch; ignore WM_SIZE's truncated packed dimensions. Other HWNDs,
thread markers and unrelated message IDs do not alter resize state.
Minimized or zero extents neither resize nor render. Block with `GetMessageW`
when there is no work; quit takes precedence over a pending resize and is
never translated or dispatched. Keep WARP and hardware as distinct sources
with no driver fallback.

PostMessageW's BOOL zero result removes any already-pending WM_QUIT with
`PeekMessageW(NULL, WM_QUIT, WM_QUIT, PM_REMOVE)` before posting exit code 1;
nonzero BOOL, including negative values, means successful enqueue. This makes
the failure code take precedence even if a normal close was already queued.
The loop maps a nonzero 32-bit WM_QUIT exit code to the existing sample result 1
through normal finalization, not successful shutdown or an extent-query
fallback. WM_CLOSE uses a local MSG and `PeekMessageW(NULL, WM_QUIT, WM_QUIT,
PM_NOREMOVE)` before posting normal quit: it leaves any pending quit intact, so
PostQuitMessage(0) cannot overwrite a resize-post failure. This adds no capture
or writable global.

For a changed positive extent, clear context state, retire the RTV before its
back-buffer owner, call only `IDXGISwapChain::ResizeBuffers` slot 13 with
`HRESULT(IDXGISwapChain*,UINT,UINT,UINT,DXGI_FORMAT,UINT)` and arguments
`0, width, height, DXGI_FORMAT_UNKNOWN, 0`, then acquire a fresh back buffer
and RTV, set the corresponding viewport, bind, draw and present. The final two
arguments use Windows x64 caller stack offsets 32 and 40. Check signed HRESULT;
positive success codes are success.

Use lexical per-frame surface scopes rather than overwriting live unique
owners. Each iteration's inner `ClearState` finalizer and reverse-release
scopes must finish before the next iteration can resize.
Rebind all pipeline state after clearing. A resize, message, Present or
reacquisition failure ends the loop through finalization, never resumes with
stale state. Release a populated failed output exactly once only when that
API's reviewed failure contract establishes a live owned value; do not read
or release an unspecified failure output.
Retain a final outer `ClearState` before shader/buffer/context/swap-chain/device,
runtime-module, window and class teardown. Clearing is not GPU completion.

The unresolved boundary is whether this closed sample lowering should become
a reusable API: local-state/extent publication and repeated surface scopes,
reentrant dispatch, persistent surface owners, device-loss recovery,
DXGI flags/formats and general resize/output ownership are not settled here.
Provisional implementation evidence must stay separate from approval.
Deterministic noninteractive fixtures must cover coalescing, zero/minimized
extents, exact ABI and release/resize/reacquire ordering, failures and partial
outputs, and independent source/bound forgery rejection. Do not use a live UI
sample as the automated test fixture.

## Separate queue/local-state proof

**UNAPPROVED provisional implementation, not a resize renderer.**
[`window-resize-queue-stage.terrum`](../../samples/design/win32-dx11-triangle/window-resize-queue-stage.terrum)
adds only the closed `dx11resizequeueproof(Handle)` operation. It keeps the
existing source-defined window owner and unchanged close-only callback, creates one
hidden window, and never calls `ShowWindow` or acquires D3D11 resources.
The operation returns the count of distinct positive observed client extents
on quit, or `-1` on query/message/rectangle/counter failure; the caller's
lexical window/class cleanup runs in either case. NoThrow teardown failure
retains exit 87.

This predecessor deliberately retains its synthetic queued-WM_SIZE algorithm.
Its close-only callback does not implement the notification recommendation
above; its queue fixtures are not evidence that real OS resizing reaches a
renderer. The hidden texture/viewport/pixel predecessor chain retains this
explicit `QueuedSizePredecessor` policy and requests its own next observation.
Only that sealed chain may use the legacy policy. Actual WARP/hardware renderer
sources and bound proofs require `CallbackPostedAppSize`; removing the posting
callback or downgrading to the predecessor is rejected.

One 48-byte MSG, one 16-byte RECT and all pending/minimized/current-extent
state live in the run frame. There is no writable global, callback capture,
raw source pointer or retained native loan. The initial extent is pending.
Zero extents pause without replacing the last positive extent; restoration
to that same extent is not a new generation. Negative extents or nonzero
origins fail explicitly. Both dimensions use full 32-bit RECT fields.
Only `GetMessageW` uses signed positive/zero/negative classification;
`PeekMessageW` and `GetClientRect` use BOOL's zero/nonzero convention.
The loop has no artificial queue cap; sustained message production can defer
observation until the queue drains.

An independent source hash and bound proof node seal this entire program,
its borrowed owner handle, private import signatures and run-frame operation.
Only after those checks does validation project the node to the existing
window-owner predecessor to prove the unchanged constructor, callback,
thread guard and lexical teardown. Emission retains the new queue algorithm,
not that predecessor's ShowWindow/pump behavior. This projection proves no
COM lifetime or resize semantics. Existing fixed-size WARP/hardware and
`windows-native-ffi` seals are unchanged.

Deterministic test-only import interception exercises coalescing, latest
minimized state, zero extents, restoration, duplicate/foreign/thread events,
quit precedence, full-width dimensions above 65535, BOOL/signed-result
distinctions, query/message/geometry failure and window/class teardown.
SDK assertions check MSG/RECT layout, all queue signatures and the proposed
slot-13 ResizeBuffers signature; ResizeBuffers itself is not invoked.
Source/bound mutations reject changed handle loans, ABI/export identities,
callbacks, thread guards and cleanup. Fixtures remain noninteractive.

## Recommended composed renderer gate

**UNAPPROVED, sample-specific recommendation.** Add separate hardware and WARP
sources and a separate bound proof, not another case in the fixed-size seals.
The proof must retain and independently check the repeated lexical back-buffer
and RTV scopes, their output declarations and drops, inner ClearState, and
outer ClearState. Only then may it project unchanged setup and teardown to the
fixed-size predecessor. Native emission must use those scopes, including the
existing partial-output helpers and normal/exceptional finalization, rather
than a second manually managed COM owner stack.

Recommend one authoritative post-drain RECT snapshot for both ResizeBuffers
and the viewport. A second viewport query could observe a different extent
after resource recreation. All loop state must occupy private run-frame
storage disjoint from expression spills, output cells and cleanup state.
After ShowWindow, normalize the first positive extent with ResizeBuffers too:
the acquisition's pre-show extent is not the loop's accepted extent. Without an
exposure request, subsequent equal positive snapshots do no work. Every rendered
iteration retires its surfaces before blocking, so an event-driven sample renders only initial and
changed positive extents unless the additional exposure recommendation below
applies; it does not render continuously or on unrelated messages.

The unresolved reusable boundary remains extent publication across source
scopes, persistent surfaces, general redraw/animation, reentrant dispatch
and device-loss recovery. This closed composition settles none of those APIs.

## Additional exposure redraw recommendation

**UNAPPROVED, sample-specific recommendation, not approval of the renderer or a
reusable redraw contract.** Extend only the two separate resize stages with a
frame-local `redrawPending` bit distinct from `resizePending`. Coalesce
target-HWND `WM_PAINT` observed through either PeekMessageW or the blocking
GetMessageW path. Other HWNDs, thread messages and unrelated message kinds
must not request a frame or alter the minimized state. Translate and dispatch
paint normally: the source callback delegates paint to DefWindowProcW,
which validates the update region. Do not swallow paint or replace normal
handling with an application callback bridge. The resize signal separately
uses the catalogued PostMessageW and close-guard PeekMessageW imports.

After draining and dispatching the batch, if either request is pending and the
latest target-window state is not minimized, query one authoritative client
RECT. Reject nonzero origins and negative dimensions; suppress zero extents.
The first positive extent or a changed positive extent keeps the existing
ClearState, retired-surface, ResizeBuffers, fresh-acquisition and render path.
At an unchanged positive extent, a pending paint instead acquires a fresh
per-iteration back buffer and RTV, rebinds, draws and presents **without
ResizeBuffers**. An unchanged extent with resize alone still does no work.
Paint and resize in the same batch produce at most one frame using the final
RECT, not packed `lParam` dimensions or a second viewport query.

Consume both requests for each observation, including minimized/zero
suppression; a later same-size restoration must not replay a stale paint.
Quit preempts both requests. Keep the inner ClearState and RTV-before-buffer
retirement before waiting, all existing failure/partial-output cleanup, and
the final outer ClearState and reverse teardown. No persistent surface owners,
continuous rendering, device-loss recovery, general queue/COM API, fixed-size
seal change or scanout/pixel claim is recommended here. Real update-region
handling and real-driver exposure redraw remain distinct evidence gaps.

An opt-in exact-main WARP probe also covers and uncovers the visible test
window without invalidating its client region. On the current Windows x64 host,
uncovering produced no target `WM_PAINT` within the one-second observation
window, so the sample correctly did not schedule a same-extent redraw. This
does not establish behavior on other Windows/composition configurations or
mean that the existing explicit-update-region redraw path failed. The
recommendation is to redraw when a real target paint is observed, not to infer
that every occlusion change creates one or add continuous/polling redraw to
compensate.

**Next-step recommendation (UNAPPROVED):** retain
`Dx11VisibleWarpExposureObservesNaturalPaintBehaviorAsync` as an opt-in
observation, not as a positive repaint gate. It removes the test-only cover,
does not call `InvalidateRect` afterward, and records either a real target
`WM_PAINT` followed by DefWindowProcW update-region validation and a
same-extent frame, or no paint and no second frame. The existing single-host
observation took the latter path. A test that requires an OS-generated paint
for every controlled uncover is not deterministic on the evidence available:
the exposed window can retain its displayed contents, and this sequence has
already produced no target paint on the current host. Do not synthesize
`WM_PAINT`, invalidate after uncover, or add polling/continuous redraw merely
to make that assertion pass. Add a required positive case only after a
deterministic OS-generated repaint trigger is demonstrated without application
invalidation after uncover; until then, automatic repaint remains an evidence
gap, not an approved Windows or renderer contract.

## Queue proof's resource-lifetime boundary

The queue proof owns the complete run and reports observations only at quit.
It does not yet publish a positive extent to a source per-frame consumer or
carry queue state across that consumer's surface scopes. That conditional
publication/reentry contract and a separate bound validator for repeated
RTV/back-buffer scopes are the next boundary.

The existing presentation/pipeline helpers still authorize only the complete
one-shot owner graph. Their output helper publishes directly into an owner
cell without proving a previous owner's retirement. Full integration must
independently prove each iteration's ClearState, RTV-before-back-buffer drops,
vacant output publication, exact ResizeBuffers call, fresh acquisition and
failure finalization; erasing that loop before the fixed-size predecessor
seal would not prove those invariants. No event-driven renderer, runtime
ResizeBuffers/reacquisition evidence, hardware resize or WARP resize pixels
are claimed by this queue proof.

## Separate composed renderer evidence

**UNAPPROVED provisional implementation.** The separate
[`window-device-hardware-resize-stage.terrum`](../../samples/design/win32-dx11-triangle/window-device-hardware-resize-stage.terrum)
is the primary sample path, with a distinct
[`window-device-warp-resize-stage.terrum`](../../samples/design/win32-dx11-triangle/window-device-warp-resize-stage.terrum).
Their exact source seals independently check the resize callback, PostMessageW
import and normal-close quit guard before projecting to bind the unchanged predecessor;
the resulting new proof retains a separately validated iteration graph with
lexical output declarations, per-iteration ClearState, reverse drops and
explicit acquisition/Present failure exits. The bound proof separately checks
the exact new callback graph and appended imports before projecting the callback
to its close-only predecessor. Native emission retains the checked notification
and quit guard; it does not emit the predecessor callback unchanged.
The renderer emits the iteration graph
under an additional outer ClearState finalizer. It does not manually overwrite
live source owners or change any fixed-size helper precondition.

The closed begin/resize-or-redraw-next/viewport/result spellings are sample
lowering operations, not canonical intrinsics or a general publication API. All queue/extent state
is in disjoint run-frame storage; one post-drain RECT feeds the exact slot-13
call and slot-44 viewport. Old scopes have finished before ResizeBuffers;
nonnegative HRESULTs are accepted, and all failed or missing outputs use the
existing helpers and enclosing finalizers. No driver fallback is emitted.

Deterministic synthetic HWND/SDK-vtable interception checks initial rendering,
successful repeated resize, coalescing, minimized/zero/unchanged/foreign events,
quit precedence, full-width dimensions, signed-result conventions, query and
message errors, ResizeBuffers failure, GetBuffer/RTV missing and explicitly
owned populated failure outputs as well as unspecified outputs, Present failure,
rebindings and complete release/unload order for both source variants. Independent source/bound mutations check operation
identity, borrowed handles, ABI, slots and iteration/outer lifetime forgery.
The additional `dx11resizeorredrawnext(Handle, swapchain)` source spelling and
checked `TargetPaintAfterDispatch` bound policy now independently seal the
recommended exposure lowering. A separate run-frame bit coalesces target paint
without changing paint handling, the queue-only proof or fixed-size seals. Dispatch
finishes before the single RECT query; unchanged positive paint iterations
reuse the validated lexical acquisition/render/retirement graph but skip
ResizeBuffers. Suppressed requests are discarded before blocking.

Deterministic tests separately supply frame-acquisition and ResizeBuffers
extent plans, so same-size repaint does not assume one resize per frame. Both
drivers cover peek/blocking paint, multiple paints, unrelated/foreign/thread
messages, initial paint, both paint/size batch orders, a dispatch-mutated final
RECT, minimized/zero suppression and no stale restoration, quit, query/message
and redraw acquisition/Present failures including both explicitly owned live
failure outputs and unspecified outputs. The fixtures verify dispatch-before-query, each full rebind, ClearState and
reverse-release order. Source downgrade/handle mutations and bound redraw-policy
mutations are rejected independently. The checked-exceptions gate remains
required because recoverable failures must unwind through the renderer's
resource-cleanup scopes; FailFast does not unwind those scopes. Recommendation:
retain the exact checked-exceptions gate until equivalent fail-fast cleanup is
explicitly designed and verified. FailFast source/emission remains rejected,
not newly supported. Emitted
imports retain the fixed-size predecessor's setup/teardown imports, replacing
its one-shot pump with the existing private System32 queue bindings; exposure
adds no native import or custom application DLL.

Synthetic callbacks exercise actual emitted WM_SIZE handling during creation,
show, sent-message servicing and nested dispatch, without queueing WM_SIZE or
requiring paint. They check same-HWND posting, all SIZE states, full-width
WPARAM/LPARAM ABI, zero posted lParam, nonzero BOOL success, posting failure,
failure followed by close, default-procedure passthrough, and normal close.
Private markers are filtered and coalesced but never translated or dispatched;
foreign/thread markers and other message IDs retain ordinary dispatch.
Source and bound mutations separately reject changed signal payloads, import
identity/signatures, failure codes, quit guards and notification-policy downgrades.

The synthetic fixture creates no UI or real device and simulates paint
validation; it does not exercise DefWindowProcW's real update region,
occlusion/exposure, real-driver redraw, resized pixels or scanout. Separate opt-in
WARP
(`TERRUM_RUN_INTERACTIVE_WINDOW_TESTS=1`) and hardware
(`TERRUM_RUN_DX11_HARDWARE=1`) integrations emit their respective stages,
resize a visible HWND with `SetWindowPos`, then check process survival and
normal `WM_CLOSE` teardown. Routine test runs skip both; neither observes
resized pixels or the driver's internal `ResizeBuffers`/reacquisition calls,
and neither has been exercised here. Those visible lifecycle tests provide no
texture-dimension or resized-pixel evidence. The separate hidden extent gate
below addresses only real-WARP texture dimensions; the reusable boundaries
above remain unresolved.

A separate noninteractive exact-main integration covers the real update-region
boundary without putting a window on the desktop. Its nine-case WARP selection
checks four requested client extents, coalesced resize batches, two successive
resize generations, a size round-trip back to the current extent without an
extra `ResizeBuffers`, and same-extent paint redraw. Two cases move the visible
test HWND wholly outside the virtual desktop before showing without activation;
one invalidates the client region after repeated resize. Test-only interception
confirms the update region exists before `DefWindowProcW` and is validated
afterward, then checks the pixel-classified frame. All nine WARP cases passed.
The separately opt-in exact-main hardware case uses
`main-hardware.terrum`, requires `D3D_DRIVER_TYPE_HARDWARE` without fallback,
and passed with two coalesced resize batches, the two resize generations and
final same-extent repaint. This is **UNAPPROVED provisional implementation
evidence** for update-region
validation and redraw, not natural exposure/occlusion, on-desktop presentation,
scanout or approval of the recommendation; no authored C application bridge or
production import is added.

## Retired hidden-WARP texture extent evidence

The implementation-only hidden fixture, proof projection and custom emitter
have been retired. Current extent, viewport and mapped-pixel observations are
authored in the
[WARP mapped-readback sample](../../samples/design/win32-dx11-triangle/window-device-warp-resize-mapped-pixel-evidence.terrum)
and its hardware counterpart. The recommendation below remains unapproved;
this historical section records the former implementation, not an active path.

**UNAPPROVED recommendation and provisional implementation, not contract
approval.** Add only the closed `ID3D11Texture2D::GetDesc` catalog record:
slot 10 (x64 vtable byte offset 80),
`void(ID3D11Texture2D*,D3D11_TEXTURE2D_DESC*)`, with the borrowed texture in
RCX and writable descriptor address in RDX. The method returns void, not
HRESULT or BOOL, and acquires no reference. Its complete SDK descriptor is
44 bytes aligned to four: Width/Height at 0/4, MipLevels/ArraySize/Format at
8/12/16, SampleDesc.Count/Quality at 20/24, Usage/BindFlags/CPUAccessFlags/
MiscFlags at 28/32/36/40. An eight-byte destination is not sufficient.

The former independently source- and bound-sealed hidden fixture borrowed
each freshly acquired lexical back-buffer owner before RTV creation.
A separate proof retains that observation and its failure exit before
projecting to the unchanged WARP resize predecessor. The complete descriptor
has private 48-byte reserved storage above the predecessor's seven state
slots, followed by a separate phase/initial-dimension slot. No public struct,
descriptor address, general writable loan or caller-selected COM method is
introduced. Compare the observed unsigned Width/Height against the accepted
integer resize dimensions, not RECT fields later overwritten by viewport
float representations. Existing visible WARP/hardware sources and seals,
partial-output behavior and inner/outer cleanup remain unchanged.

The evidence variant has no WS_VISIBLE, ShowWindow or activation path. It
checks and completes the initial frame, retires its RTV/back buffer, then
calls only fixed `SetWindowPos(own HWND, NULL, 0, 0, 1024, 768, 0x0016)`
(`SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE`). Its private BOOL ABI uses
RCX/RDX/R8D/R9D and outgoing stack offsets 32/40/48. Explicitly request the
next observation because SetWindowPos can deliver WM_SIZE synchronously.
The second frame must match its single client snapshot and differ from the
initial texture in both dimensions. Zero/minimized/unchanged observations,
premature quit, mismatches and native failures return failure, not incomplete
success. Exit zero is reached only after both checked frames and lexical
surface retirement; existing finalization then tears down all resources.
No marker writer, HWND discovery, external-window manipulation, driver
fallback, authored C/C++ application bridge or blocking message wait is used.

The compile-only SDK probe independently checks all descriptor offsets,
size/alignment, slot and function-pointer types. Catalog/source/bound/emission
regressions reject altered receivers, method identity/ABI, omitted checks,
overlapping or undersized storage, visibility/resize-policy and lifetime
forgeries. Synthetic test-only HWND/vtable instrumentation writes all 44
descriptor bytes and checks initial/changed mismatches, unchanged dimensions,
partial outputs, native failures and complete cleanup. It creates no window
or real device and is not linked into the unmodified real-WARP evidence image.
The hidden real-WARP integration runs that image with system imports only
and requires its two-phase success exit.

The former gate established actual initial and changed back-buffer width/height
evidence on Windows x64 WARP. It did not establish real viewport readback,
pixels, scanout, hardware behavior, exposure or external-message delivery.
Adopting a reusable descriptor/layout/COM contract still requires explicit
approval; this implementation-only gate changes no proposal or canonical
contract status.

## Retired hidden-WARP viewport readback evidence

The implementation-only hidden fixture, proof projection and custom emitter
have been retired; the source-authored mapped-readback samples now contain
the viewport observation. The recommendation below remains unapproved, and
this section is historical rather than an active implementation path.

**UNAPPROVED recommendation and provisional implementation, not contract
approval.** The former separately sealed hidden fixture extended only the
hidden texture evidence predecessor. Keep the texture-only,
visible WARP and hardware sources and their source/bound seals unchanged.
Add only the closed sample catalog record `ID3D11DeviceContext::RSGetViewports`:
slot 95 (Windows x64 vtable byte offset 760),
`void(ID3D11DeviceContext*,UINT*,D3D11_VIEWPORT*)`. The borrowed context is in
RCX, the writable in/out count address in RDX and writable viewport address
in R8. This is `STDMETHODCALLTYPE` under the Windows x64 ABI, returns void,
has no HRESULT classification and acquires no reference.

The SDK `d3d11.h` C vtable declaration and compile-only assertions independently
pin the slot and exact function-pointer type. `UINT` and `FLOAT` are four
bytes; `D3D11_VIEWPORT` is 24 bytes aligned to four, with TopLeftX/TopLeftY,
Width/Height and MinDepth/MaxDepth at 0/4, 8/12 and 16/20. Supply capacity
one, not a null count or an unbounded array. The complete output occupies
32 reserved private bytes at run-frame slots 11/12, and the count has its
own slot 13, above all eleven hidden texture predecessor slots. Reinitialize
the count to one and poison the output FLOATs before each call. No public
viewport/count structure, writable source loan, arbitrary method selection
or generic COM/FFI API is introduced.

Observe after each successful initial/resized Draw and Present, **before**
the iteration's ClearState and RTV/back-buffer retirement. The separate
bound proof retains the exact borrowed context, readback placement, failure
exit, output storage and complete predecessor graph before projection.
Require the returned count to be exactly one; compare the six float32
representations to the emitted viewport: positive-zero origin and MinDepth,
MaxDepth exactly one, and Width/Height from the same accepted positive integer
extent using the setter's shared `CVTSI2SS xmm0,eax`/`MOVD eax,xmm0` conversion.
Do not convert the returned FLOATs back to integers or query a new RECT.
This intentionally strict evidence assertion rejects negative zero, NaN,
infinity, stale/mismatched dimensions and missing/partial output writes; it
does not propose a reusable floating equality contract.

Only completion of both texture- and viewport-checked frames, followed by
their lexical surface retirement, can return success. A readback mismatch
uses the unchanged inner/outer ClearState, reverse COM release, module unload
and hidden window/class cleanup. The own-HWND fixed nonactivating SetWindowPos
and two-phase policy remain those of the hidden predecessor. There is no
ShowWindow, WS_VISIBLE, activation, visible-window discovery/manipulation,
driver fallback or blocking message wait.

Deterministic test-only vtable/import interception checks both readbacks,
full output layout and disjoint storage, count zero/two/UINT_MAX, every
origin/depth/dimension mismatch, NaN/infinity/negative zero, stale viewport,
omitted/partial writes, populated failed acquisitions, Present/resize/query
and hidden resize failures, and complete cleanup ordering. A synthetic extent
above float32's exact-integer range checks the emitter's actual rounded
conversion rather than an integer round-trip proxy. Source/bound mutations
independently reject receiver, ABI, slot, storage, visibility, policy, ordering
and lifetime forgery. The compile-only SDK probe is not linked into the
unmodified hidden real-WARP image, whose execution uses system imports only
and requires the two checked frames' success exit.

The former gate established actual WARP rasterizer viewport state consistent with
the same accepted initial/changed extents and observed texture dimensions.
It does not establish pixels, scanout, hardware behavior, exposure handling,
external message delivery, device-loss recovery or reusable COM/output
contracts. All proposal and canonical contract statuses remain unchanged.

## Retired hidden-WARP back-buffer pixel evidence

**Historical implementation record.** This exact-source proof and its custom
emitter have been removed. Current pixel validation is authored in the
[WARP mapped-readback sample](../../samples/design/win32-dx11-triangle/window-device-warp-resize-mapped-pixel-evidence.terrum)
and
[direct-hardware mapped-readback sample](../../samples/design/win32-dx11-triangle/window-device-hardware-resize-mapped-pixel-evidence.terrum);
opt-in runtime tests cover initial/resized frames and wrong-shader negative
controls. This proposal remains **UNAPPROVED**, and the former proof did not
establish reusable FFI/COM contracts.

**UNAPPROVED recommendation and provisional implementation, not contract
approval.** The former source- and bound-sealed hidden WARP fixture
extends only the hidden viewport evidence predecessor. Keep every predecessor,
visible sample and hardware seal unchanged. Its only new source operation is
the fixed sample assertion `dx11backbufferpixelsmatch(device, context,
backBuffer)`: no public staging owner, descriptor, raw pointer, mapping,
readback function or caller-selected COM operation.

The Windows SDK `d3d11.h` declarations and a compile-only SDK probe pinned
these catalog records at the time:

| Method | Slot / x64 byte offset | Signature and x64 arguments |
| --- | --- | --- |
| `ID3D11Device::CreateTexture2D` | 5 / 40 | `HRESULT(ID3D11Device*,const D3D11_TEXTURE2D_DESC*,const D3D11_SUBRESOURCE_DATA*,ID3D11Texture2D**)`; RCX, RDX, R8, R9; signed EAX |
| `ID3D11DeviceContext::CopyResource` | 47 / 376 | `void(ID3D11DeviceContext*,ID3D11Resource*,ID3D11Resource*)`; RCX, RDX, R8 |
| `ID3D11DeviceContext::Map` | 14 / 112 | `HRESULT(ID3D11DeviceContext*,ID3D11Resource*,UINT,D3D11_MAP,UINT,D3D11_MAPPED_SUBRESOURCE*)`; RCX, RDX, R8D, R9D, outgoing stack 32/40; signed EAX |
| `ID3D11DeviceContext::Unmap` | 15 / 120 | `void(ID3D11DeviceContext*,ID3D11Resource*,UINT)`; RCX, RDX, R8D |

All use `STDMETHODCALLTYPE` under Windows x64. Reuse the checked Texture2D to
Resource pointer-preserving SDK base edge and existing texture Release record
(slot 2 / 16), not QueryInterface or a new public owner contract. The probe
also pins Release/GetDesc signatures, every 44-byte texture descriptor field,
and `D3D11_MAPPED_SUBRESOURCE`: size 16, alignment eight, `void* pData` at zero,
four-byte unsigned RowPitch/DepthPitch at 8/12. Compile-only assertions pin
format 28 (`DXGI_FORMAT_R8G8B8A8_UNORM`), staging usage 3, CPU read access
`0x20000` and `D3D11_MAP_READ` 1. This UNORM format stores successive unsigned
R/G/B/A bytes, four bytes per pixel, not BGRA or sRGB, as specified by the
[SDK DXGI format definition](https://learn.microsoft.com/en-us/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format).

Each frame retains its existing actual extent check. Before observation,
require each dimension in `[16,4096]`, one mip, one array element, RGBA8_UNORM,
and sample count/quality 1/0. Sixteen keeps the selected center pixel strictly
inside the fixed triangle rather than on a rasterization edge; 4096 bounds
this sample-only staging allocation and integer address arithmetic, not
general DX11 limits. Clone the complete private descriptor, overriding Usage
to STAGING, BindFlags/MiscFlags to zero and CPUAccessFlags to READ. Create the
staging texture with null initial data and a zero-initialized private output
cell. Treat all nonnegative HRESULTs as success, requiring a nonnull owner;
release a populated failure output only when the provider contract establishes
that the output is live and owned on failure.

After Draw and **before Present** (the discard swap chain does not preserve
contents after presentation), copy the current back buffer to the distinct
staging resource, then use blocking immediate-context Map with subresource
zero, READ and flags zero. The
[SDK Map semantics](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-map)
and [CopyResource restrictions](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-copyresource)
require compatible dimensions/format/sample layout and unmapped resources.
The blocking Map synchronizes the queued copy; no Flush, polling or
DO_NOT_WAIT is needed. Map success establishes the mapping even if its
output is subsequently rejected; failure never establishes one and its
output must not be dereferenced or unmapped.

Require nonnull pData and unsigned RowPitch at least Width times four.
Validate the final active byte with 64-bit `pData + (Height-1)*RowPitch +
Width*4-1`, rejecting pointer wrap before any read. Scan every active RGBA8
pixel, skipping row padding, and require each to match either the expected
clear color or flat triangle color within the fixed channel tolerance. Retain
the exterior clear witness at `(0,0)` and interior triangle witness at
`(Width/2,Height/2)` so both colors must occur. The
[SDK mapped-subresource contract](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_mapped_subresource)
allows row padding; DepthPitch is not a Texture2D byte-length bound and is
not used. Reuse the exact-main test's expected bytes: clear
`(20,31,46,255)`, triangle `(26,204,230,255)`, RGB tolerance one and alpha exact.
The fixed shader and clear color are part of this evidence boundary, not a
general image comparison contract.

Private run slots 14/15/16 hold respectively the staging owner, complete
mapped output, and assertion result/center address, disjoint from all
fourteen viewport-predecessor slots. Every successful Map is unmapped
exactly once before staging release, including invalid output or pixel
mismatch. Failed Map is never unmapped. Staging retirement occurs before
Present or an evidence failure return; the unchanged frame/outer ClearState,
RTV/back-buffer retirement, reverse COM release, module unload and own hidden
HWND/class cleanup then run. No source-observable native address escapes.
[Unmap invalidates the mapped pointer](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-unmap);
no pixel access is emitted after it.
Only both pixel-, viewport- and extent-checked frames can reach exit zero.

Deterministic test-only platform interception checks call ABI/stack alignment,
complete descriptors, private storage, padded/tight rows, odd dimensions,
exact dimension thresholds, signed success, Map failure with populated
output, missing/partial outputs, pointer wrap, each RGBA mismatch on either
frame, quantization tolerance, and full cleanup on every failure. Source and
bound mutations independently reject receivers, ABI/slots, storage, omission,
reordering, policy and lifetime forgery. The fixture creates no window or real
device and is never linked into the real-WARP evidence PE. Separate hidden
real-WARP runs use unmodified system imports only: the fixed shader must
complete both frames; a wrong-color shader must fail the pixel assertion.
The compile-only SDK probe is not linked into either image.

This slice establishes actual initial and resized WARP back-buffer pixel
evidence at two fixed sample positions. It does not establish scanout,
screenshots, visible-window behavior, hardware pixels, exposure handling,
external WM_SIZE delivery, device-loss recovery or reusable mapping/COM/FFI
contracts. The source remains hidden and resizes only its own HWND with the
predecessor's fixed nonactivating SetWindowPos. No visible sample or screenshot
window is launched or manipulated. Every proposal remains **UNAPPROVED**;
canonical specifications and implementation status are unchanged.

## Retired hidden direct-hardware back-buffer pixel evidence

**Historical implementation record.** This separate hardware proof and its
projection to the WARP proof have been removed. Current direct-hardware pixel
validation is authored in the mapped-readback sample linked above and remains
gated by hardware availability. This proposal remains **UNAPPROVED**; the
former sample-specific gate did not establish reusable FFI/COM contracts.

**UNAPPROVED recommendation and provisional implementation, not contract
approval.** The separately sealed hardware variant
adds a direct-hardware version of the hidden WARP pixel gate. The WARP source,
source hash, bound proof, private storage, pixel emitter and tests stay unchanged
and independently authorized. Neither visible `main.terrum` nor
`main-hardware.terrum` changes or runs as part of this evidence slice.

The new exact-source hash and outer bound proof authorize only the hardware
acquisition identity and distinct hidden-hardware phase markers. Validation
projects those identities to the complete unchanged WARP pixel predecessor;
it checks the acquisition declaration and every nested texture, viewport,
pixel, iteration and lifetime proof. After projection, both verification and
emission restore `Dx11HardwareTryCreate` before using the existing catalogued
hardware acquisition mechanism. Generated code differs from the WARP pixel
gate only in driver value 1 instead of 5. There is no WARP acquisition or
fallback branch in the hardware image.

The existing closed D3D11 session resolves `D3D11CreateDeviceAndSwapChain`
from System32. Retain its Windows x64 twelve-argument ABI: null adapter and
software module, hardware driver, zero flags, feature level `11_0`, count one,
SDK version seven, the same private window-sized swap-chain descriptor and
zeroed device/swap-chain/context output cells. Signed HRESULT handling,
nonnull-success requirements, partial-output retirement, module lifetime and
reverse resource cleanup are unchanged. No public driver selector, new
catalog method, layout, COM owner, mapping API or native boundary is added.

Reuse exactly the WARP gate's two fixed RGBA8 pixel assertions and tolerances,
`[16,4096]` dimension limits, staging/Map/Unmap/Release ordering and seventeen
private spill slots. Check exterior clear `(0,0)` and interior
`(Width/2,Height/2)` **before Present** for initial and self-driven resized
frames, using the existing sample shader. Both actual texture extents and
viewport state must also pass before the two-phase success exit. The only HWND
is the sample's own hidden window; it has no WS_VISIBLE, ShowWindow or
activation path and uses only the predecessor's fixed nonactivating
SetWindowPos on its own HWND. It neither discovers nor manipulates external
or screenshot windows.

Focused deterministic coverage checks the separate source/bound seals, exact
driver-only machine-code difference, no fallback, nested-proof forgery and
48 synthetic hardware-argument/pixel/failure/cleanup cases. The existing
test-only native fixture creates no window or real device; its results are
not hardware rendering evidence. The unchanged WARP SDK probes and pixel
regressions continue to cover the shared ABI/layout/readback mechanisms.
Two hidden real-hardware tests use unmodified system-only PEs with adjacent
HLSL and no application DLL: the original shader must complete both frames,
and a wrong-color shader must fail the pixel assertion. They require
`TERRUM_RUN_DX11_HARDWARE=1` and the existing Windows x64 hardware/desktop
preflight. Unavailable hardware is reported **not exercised**, not success;
an application failure after successful preflight is a failure, not a skip.
These tests do not require or enable `TERRUM_RUN_INTERACTIVE_WINDOW_TESTS`.

This evidence classifies the active back-buffer image into the two expected
colors and checks both witnesses per frame on the exercised hardware host. It
does not establish exact triangle coverage/rasterization, visible presentation,
scanout, screenshots, external-message delivery,
exposure handling, device-loss recovery, deployment portability or reusable
readback/COM/FFI contracts. Every proposal remains **UNAPPROVED** and canonical
specifications remain unchanged.
