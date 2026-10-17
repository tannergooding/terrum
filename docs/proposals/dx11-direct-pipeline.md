# Direct DX11 pipeline and one draw

**Status: UNAPPROVED recommendation.** This document records a provisional
Windows x64 implementation boundary. It accepts no source/FFI, catalog,
layout, ownership, callback, COM, ABI or persisted-metadata contract, and
does not change canonical specifications. The accepted staged direction
remains separate from these candidate contracts.

## Recommendation

Keep pipeline submission in separate Terrum source calls, not an
application-authored C bridge or an all-in-one renderer export. Borrow the
already acquired context, render target, input layout, immutable vertex
buffer and shaders. Bind one render target, one viewport, one POSITION
input layout, one vertex buffer with stride 12 and offset 0, triangle-list
topology and both shaders with no class instances. Submit `Draw(3, 0)` once.

Enclose binding, clear/draw, `Present` and the entire message pump in an
inner `try/finally` inside successful render-target acquisition. Its
`context.ClearState()` must run while the context is live, before any bound
resource or enclosing owner releases. Preserve reverse acquisition
teardown: render target, back buffer, pixel shader, vertex shader, input
layout, buffer, context, swap chain, device, window and class registration.
Clearing context-held references is not GPU completion or a `Flush`.

Use the exact SDK void signatures and Windows x64 ABI. Never interpret
EAX after the D3D11 binding, draw or `ClearState` calls as an HRESULT. The
sample's checked viewport helper returns a Boolean only for its separate
`GetClientRect` validation.

| Context method | Slot | Byte offset | Nonreceiver native arguments |
|---|---:|---:|---|
| `OMSetRenderTargets` | 33 | 264 | Count 1, address of an 8-byte RTV-pointer cell, null depth view |
| `RSSetViewports` | 44 | 352 | Count 1, readonly address of a 24-byte viewport |
| `IASetInputLayout` | 17 | 136 | Input-layout pointer, not its owner-cell address |
| `IASetVertexBuffers` | 18 | 144 | Start 0, count 1, buffer-pointer-cell address, stride-cell address, offset-cell address |
| `IASetPrimitiveTopology` | 24 | 192 | `D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST` (4) |
| `VSSetShader` | 11 | 88 | Vertex-shader pointer, null instance array, count 0 |
| `PSSetShader` | 9 | 72 | Pixel-shader pointer, null instance array, count 0 |
| `Draw` | 13 | 104 | Vertex count 3, start vertex 0 |
| `ClearState` | 110 | 880 | None |

`IASetVertexBuffers` has six native arguments including the receiver;
stride and offset addresses occupy caller stack offsets 32 and 40.
Home space, call alignment and all pointer/cell widths must be preserved.
The binding calls borrow source owners; D3D11 retains its own native
references according to its API, rather than taking source ownership.

## Viewport storage boundary

Recommend a closed immutable `D3D11_VIEWPORT` carrier with an exact
initializer and a readonly, synchronous no-escape address loan. Its six
float32 fields have offsets 0/4/8/12/16/20, size 24 and alignment 4. The
visible sample should derive width and height from the current client rectangle,
not from the declared outer window size. Acquisition already obtains those
dimensions for the swap-chain buffer; a reusable source-visible carrier and
address loan remain a separate **UNAPPROVED** contract decision.

The current bounded implementation keeps the carrier compiler-private:
`context.RSSetViewports(windowHandle)` calls the catalogued `GetClientRect`,
requires a successful, zero-origin, positive client extent, converts the
dimensions to float32, and builds immutable 24-byte viewport storage for the
synchronous D3D11 call. It returns false without calling D3D11 if the query
fails or yields an invalid rectangle; the source's checked failure return runs
the inner `ClearState` finalizer before releasing bound
resources. This does not
widen the general 16-byte `ValueLayout.MaximumSize` limit or expose a source
descriptor value/address. The reusable source carrier/initializer and
readonly loan remain unapproved, not silently accepted.

## Full-image color-domain recommendation

**UNAPPROVED recommendation.** Keep readback inside the exact hidden evidence
gate. After the checked staging texture is mapped, inspect every active RGBA8
pixel, skipping `RowPitch` padding, and require it to match either the fixed
clear color or the flat triangle shader color with the existing one-code RGB
tolerance and exact alpha. Preserve the exterior and center witness checks so
both expected colors must occur. Validate the active row width and final-byte
pointer addition before the first pixel read, and keep the existing
unmap/release cleanup on every outcome.

This is a bounded color-domain check for this shader, not a rasterizer or
triangle-coverage oracle. It does not add a source-visible mapping API,
general pointer access, or reusable image-readback contract. The provisional
implementation remains separate from acceptance of this recommendation.

### Exact coverage recommendation (deferred)

**UNAPPROVED recommendation.** Do not add an exact cross-host coverage mask
yet. The unchanged [`main.terrum`](../../samples/design/win32-dx11-triangle/main.terrum)
supplies `(0, c, 0.5)`, `(c, -0.5, 0.5)`, `(-c, -0.5, 0.5)`, where
`c = float32(0.6) = 0.6000000238418579`. The unchanged
[`triangle.hlsl`](../../samples/design/win32-dx11-triangle/triangle.hlsl)
passes POSITION through with `w = 1` and emits a constant color. All vertices
are inside the clip volume. For the probe's checked zero-origin viewport,
the [D3D11 viewport transform](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm#Viewport)
evaluates `X = (x + 1) * W * 0.5`, `Y = (1 - y) * H * 0.5` with float32
arithmetic: apex near `(W/2, 0.2H)`, base near `(0.8W, 0.75H)` and
`(0.2W, 0.75H)`. `W/H` are each generation's checked live client/back-buffer
dimensions, not the declared `800x600` outer window or staging `RowPitch`;
the existing hidden test requests resized extents `960x640` and `513x337`.

The checked RGBA8 back buffer has one sample per pixel. Its
[sample center](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm#PixelCoordinateSystem)
is `(column + 0.5, row + 0.5)`. D3D11 snaps vertex X/Y to exactly eight
fractional bits before coverage. The
[top-left rule](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm#Triangles)
includes this triangle's left sloping edge, excludes its right sloping and
horizontal bottom edges, and requires both incident edges to qualify at a
vertex. These rules alone do not uniquely determine the snapped vertices.

[Float-to-fixed conversion, section 3.2.4.1](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm#FLOATtoFIXED),
permits `0.6` integer-ULP error; its ideal nearest-even diagram is stricter
than the requirement. Section 15.16 refers back to that conversion, despite
describing it as round-to-nearest. At `513x337`, float32 viewport arithmetic
gives left-base X `102.59999084472656`, or `26265.59765625` in subpixel units.
Both `26265` and `26266` meet the stated tolerance. With apex
`(65664,17254)` and right base `(105062,64704)`, these left-base choices
change pixel `(187,150)` from covered to clear under the same top-left rule.
A nearest-even mask observed on one WARP version is therefore not sufficient
evidence for a cross-version WARP or hardware assertion.

Retain the conservative gate: every active pixel must belong to the existing
clear/triangle color domain, with exterior/center witnesses on both initial
and self-driven resized frames, excluding row-pitch padding. Before adding an
exact geometry assertion, resolve the snapping tolerance or prove that all
API-permitted viewport arithmetic and snaps yield the same mask at every
tested extent. Any interim geometry gate must distinguish guaranteed
interior/exterior from indeterminate boundary samples, not guess a rounding
rule or treat one host's mask as authoritative. Keep native observation
test-only; this recommendation changes no production shader, Terrum source,
canonical specification or DX11/FFI approval status.

## Implementation evidence and limits

[`window-device-pipeline-stage.terrum`](../../samples/design/win32-dx11-triangle/window-device-pipeline-stage.terrum)
is separately source- and bound-sealed. Only validated pipeline calls,
draw and the inner state finalizer project away before the unchanged
shader/input-layout predecessor seal is applied.

Test-only interception retains real WARP receivers and forwards Terrum's
emitted binding/draw calls through saved SDK tables. It checks the viewport
against the live client rectangle, resizes the test window after swap-chain
acquisition to reject a hard-coded viewport, and reads back every active
RGBA8 pixel, requiring the fixed clear or shader color with one-code RGB
tolerance and exact alpha. It retains the queried viewport-center and `(0,0)`
witnesses, skips row-pitch padding, and checks the final mapped-byte address
before scanning. Failure injection checks that an initial
client-size query failure skips device creation, and that a failed later query,
nonzero client origin, zero width and negative height skip draw while clearing
context state before releasing bound resources. Normal, Present-failure and
message-error exits retain the same cleanup checks. The ordinary emitted
application has only system imports, no test DLL or application-authored
bridge.

This is same-host Windows x64 color-domain evidence, not exact raster
coverage/geometry, physical scanout, ongoing resize handling, hardware-driver,
general COM, shipping/deployment or approval evidence. The viewport matches
the client extent when the binding code queries it; there is no resize-message
redraw or swap-chain resize support. The exact WARP and hardware pipeline stages now
have a bounded executable-relative shader-path implementation: source-authored
Terrum scans the returned UTF-16 units in one 260-WCHAR local, rejects
zero/truncated/invalid paths, preserves the final separator and writes the
fixed `triangle.hlsl` leaf. The reviewed System32 `GetModuleFileNameW` and
`GetLastError` imports are gated to those sealed stages; the query receives
one bounded writable loan and each compiler call receives a readonly
synchronous loan. Source/bound/catalog mutation checks, synthetic drive-root,
UNC, extended-prefix, surrogate-pair and 246/247 boundary fixtures, and actual
WARP CWD-decoy, missing-adjacent-asset, relocation, rendering/readback and
cleanup tests passed. This is provisional implementation evidence only: it
does not generalize strings, mutable arrays or filesystem support, and it does
not approve the FFI recommendation or any canonical contract. The standalone
shader predecessor still uses its CWD-relative literal and absolute build-host
System32 D3DCompiler 47 import. Compiler diagnostics are released without display.

## Closed runtime compiler session

**UNAPPROVED bounded implementation evidence.** Only the exact WARP and
hardware pipelines replace the compiler PE import with
`tryopenshadercompiler(out var compiler)` and the two session-bound
`compiler.D3DCompileFromFile` calls. The closed intrinsic uses
`LoadLibraryExW(L"D3DCompiler_47.dll", NULL, LOAD_LIBRARY_SEARCH_SYSTEM32)`
(`0x800`), never bare, legacy or app-local search. Two private 260-WCHAR
buffers hold `GetSystemDirectoryW` and the loaded module's full
`GetModuleFileNameW` path. Length/capacity checks reject zero or truncation;
case-insensitive `CompareStringOrdinal` validates the entire expected
directory plus `\D3DCompiler_47.dll` before resolving only the exact export.
Mismatch, including a preloaded wrong-path module, releases the acquired
reference and returns failure.

The private module and export words occupy one unique 16-byte session, not
source pointers. Its source/bound scope encloses all shader bytecode owners
and clones. The predecessor's unchanged clone/consume/drop seal is applied
only after validating and projecting this session wrapper; hardware
projection preserves it. Consequently all blob vtable calls and releases
occur before unload, including diagnostics and populated or aliased failure
outputs. The dynamic call preserves the nine-argument x64 register/stack ABI
and signed HRESULT. NoThrow cleanup terminates with exit 87 if `FreeLibrary`
fails, including rejected-reference cleanup; failure is never success-shaped.
Deterministic test-only probes exercise loader/export/path failures, exact
arguments, real preloaded app-local rejection, clones and unload ordering in
both profiles. Production has no authored C application bridge.

This does not enable general loader, source-level function-pointer, COM or
deployment APIs. The standalone shader predecessor remains a separate,
CWD-relative, same-host absolute-import gate. `d3d11.dll` deployment is
unchanged; this compiler unit is not whole-application deployment support.
These sample-specific recommendations remain **UNAPPROVED**; canonical
contract statuses remain unchanged.

## Separate hardware acquisition recommendation

**UNAPPROVED.** The requested non-WARP sample uses a distinct
[`window-device-hardware-pipeline-stage.terrum`](../../samples/design/win32-dx11-triangle/window-device-hardware-pipeline-stage.terrum)
and `trycreatehardwareswapchain(window, out device, out swapchain, out context)`.
It shares the existing three unique owner types but changes only acquisition,
not the rendering, client-derived viewport, Present/pump or ClearState-finally lifetime.
The existing WARP stage and its deterministic pixel/cleanup tests are retained.
There is no source-selectable driver enum, adapter selection, authored C
application bridge or automatic WARP fallback.

The sealed `Dx11HardwareTryCreate` selects signed four-byte
`D3D_DRIVER_TYPE_HARDWARE` value 1 instead of WARP value 5 in the shared
`D3D11CreateDeviceAndSwapChain` emitter. Null adapter/software, zero flags,
feature level `11_0`, SDK version 7, client-sized descriptor, absolute
build-host System32 import, private zeroed outputs, signed HRESULT and
nonnull-success checks, and reverse partial cleanup are unchanged.
Hardware acquisition failure returns failure; it does not retry with WARP.

An independent source SHA-256 and acquisition identity/shape/caller gate
authorize this one hardware program. Validation alone projects its exact
acquisition name/intrinsic to the WARP equivalent, keeping function indices
unchanged, before applying the unchanged WARP pipeline and predecessor seals.
The original hardware intrinsic is emitted. Compatibility helpers admit only
that separately sealed variant, not arbitrary hardware calls or modified
WARP programs.

SDK assertions check both enum values. Test-only acquisition interception
verifies hardware value 1 and every partial-output mask, missing success
output, alias and reverse cleanup. Test-only pipeline interception verifies
value 1 before explicitly supplying WARP receivers as deterministic test
doubles; its draw, pump, failure and lifetime traces are not hardware evidence.
This substitution exists only in test PE import fixups, never in production.

The real-hardware test is explicitly opt-in with
`TERRUM_RUN_DX11_HARDWARE=1`. Without opt-in, Windows x64, an accessible
interactive desktop and hardware feature-level-11 support, it is reported
**not exercised**, not successful hardware evidence. A System32 hardware
device preflight identifies `DXGI_ERROR_UNSUPPORTED`; unexpected preflight
errors fail rather than skip. After successful preflight there is no skip:
the unmodified generated system-only PE must acquire hardware, draw/Present,
pump `WM_CLOSE` and exit normally through cleanup. It imposes no WARP pixel
expectations and proves neither physical scanout nor GPU completion.
The opt-in acceptance was exercised successfully on a Windows x64 AMD
graphics host: hardware feature-level-11 acquisition, the unmodified PE's
visible window and normal `WM_CLOSE` exit passed. This is one host's runtime
evidence, not a guarantee for other adapters or environments.

This recommendation and implementation approve no general FFI, driver
selection, adapter, COM, deployment or scanout semantics. All proposal and
canonical contract statuses remain unchanged.
