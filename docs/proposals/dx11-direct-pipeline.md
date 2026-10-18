# DX11 pipeline and image-evidence scenarios

**Status: UNAPPROVED application/evidence recommendations.** These scenarios
exercise generic native mechanisms; they prescribe no compiler-owned DX11
catalog, source seal, special carrier or synthesized renderer. Current
maintained sources and historical stages are distinguished in the
[sample reference](../../samples/design/win32-dx11-triangle/README.md).

## Source-authored submission and cleanup

Author pipeline binding, draw, Present and message handling as ordinary
Terrum calls through authored ABI declarations. Resource wrappers and
reverse-acquisition cleanup are library/application code. An inner
`ClearState` finalizer runs while the context and bound owners remain live,
before releasing render-target/back-buffer owners or enclosing resources.
Clearing releases context-held bindings; it establishes neither GPU completion
nor a Flush.

The maintained shader uses `SV_VertexID` and needs no vertex buffer/input
layout. The older POSITION-input scenario remains an optional coverage case,
not a required source shape. Its binding data are:

| Context method | Slot | Windows x64 byte offset | Nonreceiver arguments |
|---|---:|---:|---|
| OMSetRenderTargets | 33 | 264 | Count 1, RTV pointer-cell address, null depth view |
| RSSetViewports | 44 | 352 | Count 1, readonly viewport address |
| IASetInputLayout | 17 | 136 | Input-layout pointer |
| IASetVertexBuffers | 18 | 144 | Start 0, count 1, buffer-pointer cell, stride cell, offset cell |
| IASetPrimitiveTopology | 24 | 192 | Triangle-list value 4 |
| VSSetShader | 11 | 88 | Shader pointer, null instance array, count 0 |
| PSSetShader | 9 | 72 | Shader pointer, null instance array, count 0 |
| Draw | 13 | 104 | Vertex count 3, start 0 |
| ClearState | 110 | 880 | None |

For the POSITION case, stride is 12 and offset zero. The six-argument
IASetVertexBuffers call places the final arguments at caller stack offsets
32 and 40. These SDK facts belong in authored declarations/fixtures, not
compiler recognizers. Binding/draw/ClearState return void; EAX is not an
HRESULT. Generic ABI tests also need unrelated methods and signatures.

A D3D11 viewport has six float32 fields at offsets 0/4/8/12/16/20, size 24,
alignment 4. Derive its dimensions from the checked live client rectangle,
not the requested outer window size. A reusable fixed-layout value and its
synchronous address loan use general layout/backing contracts, not the
retired `RSSetViewports(windowHandle)` compiler adaptation.

## Image-evidence recommendation

After a successful staging Map and before Present, inspect every active
RGBA8 pixel, excluding RowPitch padding. Check row width, total extent and
the final byte's address before reading. Require the selected clear or flat
shader color, one-code RGB tolerance and exact alpha, with exterior and
interior witnesses so that both colors occur. Unmap exactly once after a
successful Map on every outcome; never Unmap an unsuccessful mapping.
Pointer observation, output validity and cleanup remain authored contracts.

This establishes a bounded color domain, not exact rasterization, visible
presentation, scanout, deployment portability or a reusable mapping API.
The [resize proposal](dx11-event-driven-resize.md) adds per-generation and
paint scenarios without turning those observations into compiler policy.

### Exact coverage remains a separate decision

An exact cross-host coverage mask needs more evidence than the top-left rule.
For the historical POSITION fixture, `c = float32(0.6)` is
`0.6000000238418579`; its vertices are `(0,c,0.5)`, `(c,-0.5,0.5)` and
`(-c,-0.5,0.5)`, with clip-space w equal to 1. At a checked zero-origin
viewport, float32 transformation places the apex near `(W/2,0.2H)` and the
base near `(0.8W,0.75H)` and `(0.2W,0.75H)`. Pixel centers are half-integers,
and coverage uses eight-fractional-bit snapped coordinates and top-left
inclusion.

The [D3D11 float-to-fixed rule](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm#FLOATtoFIXED)
permits 0.6 integer-ULP error, wider than its ideal nearest-even illustration.
At 513x337, left-base X becomes `102.59999084472656`, or `26265.59765625`
subpixel units; both 26265 and 26266 satisfy that tolerance. With apex
`(65664,17254)` and right base `(105062,64704)`, those choices change pixel
`(187,150)` from covered to clear under the same top-left rule. One WARP
version's nearest-even result is therefore not a cross-host oracle.

Before asserting exact geometry, prove a common mask over every permitted
transform/snap at each tested extent, or distinguish guaranteed interior,
guaranteed exterior and indeterminate boundary samples. Do not transfer the
historical fixture's geometry to a changed shader without recomputing it.

## Integration and evidence boundaries

Use the [generic native proposal](trrm-native-interop-metadata.md), ordinary
output/ownership rules and [authored unsafe boundary](unsafe-ffi-vtable-cleanup.md).
The substantive replacement is general lowering plus source-authored calls,
not another minimal addition to a sealed sample chain.

Keep ordinary tests headless. Hidden WARP, visible-window and hardware
execution require their explicit documented gates, deadlines and owned-process
cleanup. Distinguish SDK layout probes, successful image emission, supervised
execution and actual image observations. Excluded historical DX11 prototypes
are not active coverage; this proposal supplies required cases, not fresh
qualification results.
