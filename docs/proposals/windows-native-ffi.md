# Sample-driven Windows native FFI

**Status: Unapproved**

## Summary

The Win32 + Direct3D 11 triangle is a design sketch, not a compiling program.
Its current source produces parser diagnostics, and the implemented Windows x64
compiler has no general source interop. The hosted native emitter already
supports explicit DLL/export bindings for external declarations in a verified
reference-bearing program; module packages validate Terrum declaration
contracts, but the low-level bindings themselves identify names only. This is
not a source-level C prototype or proof of foreign ABI compatibility. The
accepted grammar describes `extern` callables and raw `ptr<T>` values, while
registered import identities, target ABI mappings and the implementation
remain incomplete. `Blittable` is an accepted
representation concept, not a completed Windows FFI profile.

The sample is a useful end-to-end target for a narrow native FFI milestone.
Making it work requires more than a parser fix: checked scalar imports,
sample-specific native storage and pointer access, a static callback boundary,
exact COM interface calls and ownership, plus entry-point, diagnostics,
asset-path and runtime integration. Its API sketch can bound the declarations
and helpers without designing all of Windows or COM.

## Question

Should a bounded Windows x64 FFI milestone use the DX11 triangle as its
motivating integration target, and implement only the native boundary features
needed by that sample rather than general-purpose interop?

## Recommendation

Adopt the sample as a staged target within the existing runtime/safety and
separate-compilation roadmap areas, without adding a fifth area or changing
accepted language contracts. Keep the initial support Windows x64-only and
advance one independently validated gate at a time. The accepted external
callable identity, calling-convention and full-callable-contract requirements
remain in force; ABI annotations cannot erase effects, access, ownership,
rooting, loans or failure-containment obligations. In particular, `Sequential`
alone does not establish a foreign representation, and `unsafe` does not
authorize a proven lifetime escape.

1. **Checked scalar imports.** Implement an AOT-only source profile for
   bodyless top-level and static `extern` declarations using one registered
   Windows x64 binding profile, initially limited to fixed-width integer
   arguments/results and `void`. Use one nonrepeatable `NativeImport` semantic
   attribute with one required string-literal key, valid only on those
   declarations. Its sole first-gate identity is the fully qualified,
   compiler-supplied `Terrum.Interop.NativeImport` declaration. Treat that
   name as lookup only: resolve it to a compiler-owned semantic declaration
   record linked to the selected schema, and reject conflicting or ambiguous
   source declarations. Check the decoded string constant's type and encoding
   through the ordinary constant rules. The key selects a registry entry; it
   is not a DLL/export name or path, and neither its token spelling nor the
   attribute name grants import authority. An
   immutable, selected binding catalog maps each key to one reviewed exact
   scalar signature, ABI identity, declared effects, DLL/export and trusted
   dependency/deployment policy. The source declares the callable signature
   and ordinary effect/access/unsafe modifiers, which must match the catalog
   record; the key cannot override that contract or provide DLL/export names.
   Reject unknown keys and any signature/effect mismatch. A DLL
   basename alone does not establish trusted loading or prove that a foreign
   provider implements the declared signature. Keep the initial catalog
   compiler-supplied and    immutable, with only reviewed system imports and a
   test-only fixture entry; do not add plugin discovery, arbitrary caller
   catalogs or source-selected DLLs. Carry the catalog identity from binding
   through IR and target lowering to PE emission, and enforce
   declared effects in mandatory verification before emission. Reuse the
   existing PE import path only behind these checks; Terrum package
   compatibility is not foreign-provider proof. Do not infer symbols from
   source names or accept a caller-supplied DLL path. Gate with a Terrum AOT
   caller importing a selected system function such as `GetCurrentThreadId`
   and an independently compiled C fixture that exercises register and stack
   arguments and signed and unsigned results. Explicitly reject these
   declarations from unsupported semantic-module, TRRM, package and JIT paths.
   Keep pointers, callbacks, variadics, floating signatures and
   aggregate-by-value FFI unsupported.
2. **Sample-specific native storage and pointer calls.** Add only the native-
   sized values, opaque handles, qualified pointers, narrow fields,
   `float32` storage, fixed inline arrays and SDK descriptor layouts required
   by the sample. Descriptor layouts larger than 16 bytes need their own
   bounded native-storage contract; this does not widen the separate,
   unapproved [public fixed-layout profile](public-fixed-layout-profile.md).
   Define initialization, restricted zero-initializable carriers, stable
   terminated literals and synchronous no-escape address loans that preserve
   lifetime and read/write authority. Do not add arbitrary pointer arithmetic,
   marshalling, or pointers into movable managed storage. Use a small
   `MessageBoxW` program as an observable pointer-call gate before callbacks.
   Keep Terrum `bool`, Win32 `BOOL`, HRESULTs, handles and managed references
   distinct.

### DX11 SDK layout reference (provisional)

**Summary:** The sample's native descriptor and message structures now have
an x64 C layout probe compiled against the Windows SDK selected by the test
machine's `vcvars64.bat`. It asserts size, alignment and accessed field
offsets for `DXGI_MODE_DESC` (28/4), `DXGI_SAMPLE_DESC` (8/4),
`DXGI_SWAP_CHAIN_DESC` (72/8), `D3D11_BUFFER_DESC` (24/4),
`D3D11_SUBRESOURCE_DATA` (16/8), `D3D11_INPUT_ELEMENT_DESC` (32/8),
`D3D11_VIEWPORT` (24/4), `RECT` (16/4), `WNDCLASSEXW` (80/8) and `MSG`
(48/8). The probe passed against Windows SDK 10.0.28000.0 on the current
host.

**Recommendation:** Retain this probe as the sample-specific ABI oracle when
implementing descriptor storage. Matching SDK layout is necessary but does
not define Terrum initialization, zero-initialization eligibility, pointer
loans, ownership or general `Blittable` support; those remain separate
contracts under this unapproved proposal.

3. **One static window callback.** Support the sample's exact noncapturing
   `WNDPROC` signature, calling convention and native-entry address formation,
   not general delegates or closures. Retain code and data lifetime through
   final window destruction and class unregistration, including synchronous
   reentry during creation and teardown. Contain supported failures before
   returning to Win32 or terminate; private checked-failure catches are not a
   general callback fault barrier. Gate with a create/paint/close window and
   exactly-once, creating-thread cleanup.
4. **Closed COM and resource slice.** Bind only SDK-selected interface IDs,
   inherited vtable positions and fixed-signature calls used by this sample.
   Require explicit receiver/access contracts and valid interface upcasts;
   use closed interface-specific owners and output slots if general generic
   structs are not yet supported. Check native output ownership on failure,
   make `Take` transfer without a throwing gap, clear context bindings before
   releasing resources, and reject guessed layouts or interface resemblance.
5. **Application integration gate.** Deliver a supported port, not a claim
   that the current sketch compiles unchanged. Implement only its required
   entry/argument materialization, terminal output, diagnostics, asset-path
   conversion and COM owners/output slots; use closed interface-specific
   owners if general generic structs are not available. Then compile and run
   the triangle on Windows x64 with hardware or explicit WARP selection, the
   adjacent HLSL and D3DCompiler 47. Preserve tested window/device failure,
   shader-diagnostic, device-removal and cleanup behavior. Do not silently
   add automatic fallback. This gate does not establish support for other
   SDKs or targets.

### Proposed first pointer-call subgate (unapproved)

**Summary:** The first pointer-call slice should expose only readonly
`ptr<char16>` values produced from compiler-validated UTF-16-NUL string
literals in immutable executable-image storage. The producer proves encoding,
initialized extent, the final terminator and image lifetime; it does not grant
ownership, pinning, rooting or general pointer formation. General pointer
storage, arithmetic, dereference, managed-storage addresses and propagation
remain unsupported.

**Recommendation:** Use an explicit compiler-owned literal form, with
`utf16z("...")` as the candidate spelling, accepted only for a catalogued
readonly `ptr<char16>` parameter and a single string literal. Bound the
aggregate encoded UTF-16 data, including terminators, to 8 MiB and reject
embedded NUL rather than silently truncating text. Keep this identity, bound
and catalogued no-retention parameter contract provisional under this
proposal. First validate the static-storage/type/import path with an
independently compiled C reader fixture; then add the `MessageBoxW` observable
gate with a closed opaque `HWND` carrier and a named no-owner sentinel mapped
to native zero. Do not treat a raw pointer type, `unsafe`, or `[NoThrow]` as
proof that a foreign callee will not retain a pointer.

The general relationship among `HWND`, optional handles, `null` and
`ptr<void>` remains unsettled and is not a prerequisite for the static-literal
fixture. The proposed `HWND` carrier is sample-specific; its no-owner
sentinel does not establish general handle nullability or zero-initialization.
For production system imports, a catalog's `System32` policy is a reviewed
deployment assertion, not a PE-enforced search path. Each DLL entry must have
an explicit trusted-loader basis; do not claim the policy enum alone prevents
application-directory or other loader redirection.

**Progress:** A test-only native ABI fixture now exercises the static-literal
pointer path with an independently compiled C reader, including UTF-16 code
units, terminators, alignment, register and stack argument positions, and
scalar arguments. This establishes fixture coverage for the literal contract,
not general pointer support. Production `MessageBoxW` progress and its
sample-specific handle representation are tracked in the following subgate;
the overall proposal remains unapproved.

### Proposed `MessageBoxW` owner-handle subgate (unapproved)

**Summary:** The observable pointer-call gate needs the exact Windows x64
`MessageBoxW(HWND, LPCWSTR, LPCWSTR, UINT) -> int` shape. Its owner is optional;
native zero means that the message box has no owner. Modeling that argument as
an integer, `ptr<void>`, or general `null` would blur distinct handle and
pointer contracts. `MessageBoxW` is modal, so unattended tests must not call
the production import and wait for user interaction.

**Recommendation:** Use one compiler-owned nominal identity, provisionally
`Terrum.Interop.Windows.HWND`, with `Terrum.Interop.Windows.HWND.NoOwner` as
the only source form. Lower it to an eight-byte zero but keep it distinct from
integer, pointer and managed-reference values. Permit the dedicated no-owner
expression only as a direct argument to the owner parameter of the exact
catalog record; mandatory verification must recheck the record and mapped
parameter ordinal. Reject constants, forwarding, computations, conversions,
comparisons, returns, locals, fields, generic use and writable/optional
qualifications. Do not model the sentinel as a static field or getter.

Keep both text parameters on the existing static readonly/no-escape UTF-16
literal path. The reviewed signature is
`HWND, ptr<char16>, ptr<char16>, uint32 -> int32`; preserve the result,
including zero on failure. `[NoThrow]` describes Terrum's failure effect, not
absence of blocking, native faults, reentrancy or Win32 failure. Use a test
catalog and independent C fixture to validate argument ABI without showing a
dialog, then compile a runnable production sample and perform an explicit
interactive smoke test before claiming the observable gate.

The Windows loader documentation says the OS uses its copy when a DLL is in
the running system's KnownDLL set, which is checked before ordinary
application-directory, current-directory and `PATH` search. The current
development host is Windows 11 x64 build 26300 and lists `user32.dll` in its
KnownDLL registry key; that is host evidence only. The set is OS-dependent,
and DLL redirection, SxS redirection and loaded-module reuse precede the
KnownDLL check. The catalog's `System32` policy does not enforce a path.

**Trusted-loader acceptance condition:** The directed target scope is all
Windows x64 desktop-class NT releases supported by the project, relying on
`user32.dll` being a KnownDLL throughout that target family. This is the
product's target premise, not a compiler-host registry matrix. Assume a
trusted OS installation and normal process startup; application-controlled
DLL/SxS redirection and substituted providers are outside the gate. The
development host's nonmodal static-import probe must resolve the system image
despite application-directory, current-directory and `PATH` shadow files.
The PE's `System32` policy remains a reviewed deployment assertion, not an
enforced search path, and does not authorize other DLLs.

**Progress:** The test-only `HWND.NoOwner` source marker binds only as the
direct owner argument and lowers to native zero. Its independent C fixture
now returns a distinct failure value for nonzero owners, so the ABI check
cannot mistake an invalid owner for the expected `IDOK` result. A separate
nonmodal static-import probe passed on the Windows 11 x64 development host
(build 26300), despite application-directory, current-directory and `PATH`
shadow DLLs. The production catalog now includes the exact `MessageBoxW`
signature under the directed desktop target scope, and
`terrumc build-native-import` emits a runnable sample while ordinary `build`
remains unchanged. The opt-in smoke image was launched and returned success,
which the sample does only after `MessageBoxW` returns `IDOK`. The user
confirmed the expected text and caption appeared and dismissed the dialog with
OK, closing this observable gate on the current host. The representation and
import remain provisional under this unapproved proposal; this does not
establish general FFI or the DX11 sample.

See Microsoft's [`MessageBoxW` contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-messageboxw)
and [DLL search order](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-search-order)
documentation.

Start AOT-only and explicitly reject this surface in unsupported persistence
and JIT paths rather than dropping its contracts. At every gate, source
grammar, semantic verification, binding metadata, emitter imports and runtime
tests must agree; a host-only workaround is not a compiler support claim.
Tests can use a small native fixture before the full graphics sample is
runnable. The triangle passes floating data through pointers and descriptors,
so floating scalar calling-convention support is not an initial prerequisite.

## Exclusions

This is not general P/Invoke or marshalling; it does not define arbitrary C or
C++ ABI compatibility, variadic calls, foreign static data, x86 or non-Windows
profiles, floating scalar calls in the first gate, arbitrary callback capture,
general COM discovery, a full Windows SDK binding generator, or a stable
cross-version managed ABI. Raw pointers do not root, pin or validate their
referents. No source grant, native package, or existing private native ABI
silently authorizes these features.

The sample does not move Terrum's scalar frontend beyond the hosted verifier
and emitter. A successful build is not evidence that the self-host gate passes.
