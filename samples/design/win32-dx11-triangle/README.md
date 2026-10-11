# Win32 + Direct3D 11 triangle

[Source](main.terrum) | [HLSL](triangle.hlsl) | [helper API](interop.api.terrum) | [sample index](../../../docs/samples.md)

**Design source, not a built program or supported compiler sample.** The
repository now has a bounded Windows x64 native compiler and runtime, but does
not implement this sketch's COM, Win32, DXGI or Direct3D 11 interop surface.
See the current [compiler support boundary](../../../docs/implementation.md)
and [native profile](../../../docs/implementation/native.md). Proposed target: Windows desktop,
x64 first; feature level 11.0, fixed-size window, one immediate context.
Default hardware device; `--warp` explicitly selects software. No automatic
driver fallback, resizing, device recreation, multithreaded rendering or
application-state callback capture.

The flow follows [Microsoft's Tutorial02](https://github.com/walbourn/directx-sdk-samples-reworked/tree/main/Direct3D11Tutorials/Tutorial02), with independent Terrum source and different ownership/error handling. See [NOTICE](NOTICE.txt). Keep `triangle.hlsl` beside the eventual executable and provide the D3DCompiler 47 runtime; shader compilation here is D3DCompiler, not a Terrum compiler.

## Organization

`Program.Main` validates the optional `--warp` argument and delegates to
`TriangleApplication.Run`, which owns window/device setup and the message
loop. `RenderFrame` isolates the per-frame clear, draw and present operations;
`CompileShader` keeps shader compilation and diagnostics together. The
`WindowProcedure` remains a static, noncapturing native callback. This
organization makes the application lifecycle and per-frame work visible
without implying compiler support for the sketch's native interop surface.

## Visible boundaries

`RegisterClassExW` / `CreateWindowExW`, the message pump and native callback are explicit. Device/swap-chain creation, back buffer, render target, shaders, input layout, immutable vertex buffer, viewport, binding, draw and present are all in the source.

- `unsafe` authorizes native pointer/ABI obligations, not unrestricted recovery. The callback catches only to terminate; exceptions never unwind into Win32. It captures no managed state. A future stateful callback needs retained roots/authority and unregister/drain-before-release.
- `utf16z("...")` and `utf8z("...")` illustrate call-form targets for immutable static encoded literals with a trailing zero and no embedded zero. Their target parser, storage and pointer-lifetime checks are not implemented by the current executable profile; they do not choose a runtime string encoding.
- `InlineArray<T, N>` is contiguous inline storage, not managed `T[]` and not a SIMD portability type. Taking addresses does not allocate or silently pin.
- `[Layout(Sequential)] Vertex` has three binary32 fields, offsets 0/4/8, size 12 and alignment 4 in this ABI. Its input descriptor names the matching `R32G32B32_FLOAT` format. `CreateBuffer` copies all 36 input bytes before return.
- Local pointer arrays/descriptor storage remain live for the immediate native calls. Shader blobs remain owned through shader/layout creation. D3D retains its own references to bound resources; the context guard clears them before source owners release.

## Ownership helpers

| Helper | Required contract |
|---|---|
| `ComOwner<TInterface>` | One owned COM reference, noncopyable. `Clone` explicitly calls `AddRef`; destruction calls `Release` once. `Pointer` is a nonowning raw-pointer projection; use requires valid ownership/lifetime. |
| `ComSlot<TInterface>` | Native-addressable, zero-initialized output cell. `Address()` requires an empty cell and an exclusive no-escape call loan; the cell cannot move during that loan. Cleanup releases any populated pointer, including partial results on failure. |
| `Take()` | Requires a populated slot; moves the reference to an owner and clears the slot. No `AddRef`, allocation, callback or throwing transition after transfer starts. Empty success output is a binding/protocol fault, never an invented owner. |
| `WindowClassOwner` | `Adopt` is allocation-free/no-throw for a checked successful registration. Unregisters the atom with its instance, after all windows and callbacks are finished. |
| `WindowOwner` | Allocation-free/no-throw adoption of one checked HWND. Destruction calls `DestroyWindow` once on its creating thread. `WM_CLOSE` only posts quit; it does not destroy or consume this owner. |
| `ContextBindingsGuard` | No-throw construction; the raw pointer leaves context lifetime as an unsafe caller obligation. Here lexical cleanup keeps the context alive through `ClearState`. No duplicate COM ownership or hidden `AddRef`. |
| `NativeUtf16Z` | Explicit materialization of an application-directory-relative asset path into owned, stable UTF-16 terminated storage. Rejects embedded zeros. Releases native storage after shader calls. |

Owner cleanup is no-throw/non-suspending. Invalid window teardown is fail-fast rather than ignored. Bindings preserve creating-thread/context affinity; these types do not unconditionally satisfy `[Send]`/`[Sync]`. GPU command completion is not promised by `ClearState` or dropping a source COM reference; D3D owns the remaining native work/lifetimes.

Resource adoption and cleanup are trusted implementations, not ordinary `struct` destructors guessed by the compiler. The sketch's `unique` declarations publish those cleanup obligations in metadata. Every successful output immediately gains an owner; still-populated output slots cover intermediate failures. Reverse lexical cleanup clears bindings, releases resources/swap chain/context/device, destroys the window, then unregisters its class.

## Native package contract

Public namespaces group APIs by function: `Terrum.Interop.Windows` and `Terrum.Interop.DirectX`; package/runtime layers do not appear in their names. `where TInterface : [NativeComInterface]` is a checked ABI predicate, not a behavioral interface named `NativeComInterface`. Only an explicit compatible native interface declaration satisfies it; method names or an attached attribute cannot synthesize COM identity or layout.

SDK bindings are declaration inputs, not additional handwritten demo code. They must include the Win32 imports/types/constants used here; DXGI and D3D11 descriptor layouts/enums; `IUnknown` and the inherited COM slots through `IDXGIObject`, `IDXGIDeviceSubObject`, `IDXGISwapChain`, `IDXGIFactory`, `ID3D11Device`, `ID3D11DeviceContext`, resources, views, shaders/layouts and `ID3DBlob`; and `D3DCompileFromFile`.

Publish exact Windows signatures, interface IIDs and base slots, x86/x64 call conventions, C integer widths, SDK versions and import libraries. Binding wrappers expose the C-compatible interface upcasts used by `CreateRenderTargetView`; a pointer to an unrelated interface is not interchangeable. `HMODULE`/`HINSTANCE` have the SDK-compatible relationship, and handles/BOOL/HRESULT are not confused with managed references or language `bool`.

The sketch's handles are SDK-compatible opaque pointer types; `null` denotes their native zero value, not arbitrary struct initialization. Native signatures retain const/input versus writable/output contracts. A `write ptr<ptr<T>>` output grants writes to the pointer cell, not to the resulting `T` referent. Taking an address of read-only input does not authorize the callee to write it. The COM vtable receiver-access contract is not fully expressed by this sketch and must come from checked external-interface metadata, not be inferred from the output slot.

Raw pointer transport alone does not require unsafe access. Members declared `unsafe` require unsafe call sites: the helper signatures mark ownership adoption, the context-lifetime guard and raw diagnostic-blob access accordingly. The main program already performs these calls inside its native boundary. `Adopt` requires actual unique ownership, not merely a successful nonzero-handle check.

`*.Zero` is available only on explicitly zero-initializable *native carrier* descriptors; it does not make that descriptor valid for every API call and is not universal `default(T)`. Native `out` cells start as null. An `[OutOwnedCom]` binding must guarantee that any populated output is either transferred ownership or null, including its documented failure behavior; unspecified garbage cannot be passed to `Release`.

The `NativeExport` callback returns native `LRESULT` (`nint`); `WPARAM` is `nuint`, `LPARAM` is `nint`, and message IDs are `uint32`. The compiler creates an ABI-correct static entry, not a marshalled capturing delegate. This annotation establishes a callable native address; it need not export a named DLL symbol or make the implementation public to Terrum callers. Named exports need an explicit symbol/export contract. Static literal/IID addresses reside in stable module data; the module must remain loaded while registered callbacks can run.

`NativeError` turns failed setup HRESULTs or checked Win32 failures into ordinary exceptional propagation with operation/error data. Win32 last-error capture must occur at the import boundary before diagnostic/runtime work; APIs without meaningful last-error values use an operation failure rather than invented detail. Presentation handles occlusion and device removal/reset explicitly; removal is reported and exits with 3. Other failed HRESULTs propagate. No unsafe catch-and-continue renderer.

Compiler diagnostics are length-bounded by `ID3DBlob.GetBufferSize`, decoded as UTF-8 with explicit replacement for invalid bytes, and written to stderr; they do not require an assumed terminator. Optional final NUL is excluded from displayed text. The blob is released whether compilation succeeds or fails. `FailFastCurrentException` returns `void` with checked `[DoesNotReturn]` and `[NoThrow]` contracts, plus nonallocating fallback diagnostics. No extra source type or ABI return payload is needed.

## Deferred, not hidden

This sample exercises later interop/cleanup work, not the first kernel: native export spelling, COM slot-address loans, qualified pointer signatures and core intrinsic inline-array syntax still need frontend lowering rules. A .NET IL bootstrap must demonstrate these contracts through native-call shims or reject the sample's unsupported features. No claim of compilation or rendering is made.
