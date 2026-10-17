# Sample-driven Windows native FFI

**Status: historical staged proposal; only the Windows x64 integration
direction and motivating DX11 target were accepted.** Compiler-owned API
catalogs, sample-specific layout/carrier admission, COM ownership/acquisition
and application recipes below are withdrawn as forward recommendations.
Generic native-callable entry support is an approved direction, not approval
of the exact callback forms or metadata in these historical stages.

## Current decision boundary

Use [unsafe FFI cleanup](unsafe-ffi-vtable-cleanup.md) for the settled unsafe
and library/compiler boundary, and the
[standard TRRM native extension](trrm-native-interop-metadata.md) for concrete
unapproved import, native-entry, function-pointer, vtable and dependency
records. The latter recommends evolving the early TRRM draft, not treating
missing current assignments as a reason to reject the route.

Native declarations must be ordinary ABI-shaped library/source data. SDK
layouts, symbols, IIDs, reference-count policy, safe wrappers and application
control flow stay authored outside compiler policy. No sample name, catalog
key or exact application pattern can substitute for generic verification.
An authored integer/pointer declaration such as the GetClientRect boundary
must be supportable without the compiler recognizing that API.

The remaining sections preserve the earlier staged design and its historical
evidence, not current admission rules or a fresh support audit. In particular,
their present-tense sketch/support statements, closed catalog recommendations,
host observations and reported probes must not be cited as verification of
the current patch. Consult the implementation references for support, and
requalify the final compiler/input revision before claiming execution.
Native output/failure/cleanup composition is covered separately by the
[joint proposal](trrm-output-failure-ownership.md#proposed-native-boundary-composition);
unsafe does not erase known backing or cleanup obligations.

## Historical summary

The Win32 + Direct3D 11 triangle is a design sketch, not a compiling program.
Its current source produces parser diagnostics, and the implemented Windows x64
compiler has no general source interop; see the current
[development native profile](../implementation/native.md#semantic-and-target-boundaries). The
hosted native emitter already
supports explicit DLL/export bindings for external declarations in a verified
reference-bearing program; module packages validate Terrum declaration
contracts, but the low-level bindings themselves identify names only. This is
not a source-level C prototype or proof of foreign ABI compatibility. The
accepted grammar describes `extern` callables and raw `ptr<T>` values; see
the [external-callable source contract](../specs/grammar-spec.md#512-modifiers-cleanup-and-finalization).
Registered import identities, sample-specific gates and their implementation
remain incomplete. The
[Blittable contract](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations)
and [Windows x64 target profile](../specs/targets/windows-x64.md) now define
the fixed rootless ABI boundary, not this proposal's import catalog,
callbacks, COM, startup or sample support.

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

## Accepted roadmap direction

The staged Windows x64 direction uses the DX11 triangle as a target within the
existing roadmap areas; see the [roadmap](../roadmap.md). This accepts the
target and gate sequence only, not gate contracts or current sample support.
The accepted [external-callable source contract](../specs/grammar-spec.md#512-modifiers-cleanup-and-finalization),
[layout and ABI contracts](../specs/data-layout-and-abi.md#profile-specification-and-implementation-work),
and [lifetime and access contracts](../specs/lifetimes.md#definitions-and-notation)
remain authoritative; the individual FFI choices below are unapproved.

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
   dependency/deployment policy. Compare the declaration's
   [complete external-callable contract](../specs/grammar-spec.md#512-modifiers-cleanup-and-finalization)
   with the catalog record, including its signature, ABI identity and declared
   effect/access/unsafe obligations; the key cannot override that contract or
   provide DLL/export names.
   Reject unknown keys and any signature/effect mismatch. A DLL
   basename alone does not establish trusted loading or prove that a foreign
   provider implements the declared signature. Keep the initial catalog
   compiler-supplied and immutable, with only reviewed system imports and a
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
   terminated literals and synchronous no-escape address loans under the
   accepted [lifetime and access contracts](../specs/lifetimes.md#definitions-and-notation).
   Do not add arbitrary pointer arithmetic, marshalling, or pointers into
   movable managed storage. Use a small
   `MessageBoxW` program as an observable pointer-call gate before callbacks.
   Keep Terrum `bool`, Win32 `BOOL`, HRESULTs, handles and managed references
   distinct.

### Proposed Stage 2 storage and pointer-call profile (unapproved)

**Boundary:** These recommendations build on the accepted
[layout and system-ABI contracts](../specs/data-layout-and-abi.md#profile-specification-and-implementation-work),
[qualified access](../specs/lifetimes.md#passing-and-storage-access) and
[backing/lifetime rules](../specs/lifetimes.md#three-independent-dependencies),
and [external-callable source contract](../specs/grammar-spec.md#512-modifiers-cleanup-and-finalization);
they are prerequisites, not Stage 2 choices. The unapproved profile below
proposes sample-specific carrier, storage and pointer behavior only.

The fixed-width imports and sample-specific static UTF-16/`HWND.NoOwner`
gates are provisional implementation, not approval or general handle/
pointer support. The SDK probe is foreign ABI evidence only. The following
choices propose the next AOT-only slice; they are not implemented support or
a public stable-ABI guarantee. A supported port may change the sketch's helper
spellings without changing these contracts.

#### Carrier and storage admission

**Summary:** The sample needs native-addressable values beyond the compact
16-byte subset, but not arbitrary user-defined Windows layouts.

**Recommendation:** Admit closed, catalog-owned identities for the SDK
carriers
`DXGI_SWAP_CHAIN_DESC`, its `DXGI_MODE_DESC`/`DXGI_SAMPLE_DESC`/
`DXGI_RATIONAL` members, `D3D11_BUFFER_DESC`, `D3D11_SUBRESOURCE_DATA`,
`D3D11_INPUT_ELEMENT_DESC`, `D3D11_VIEWPORT`, `RECT`, `WNDCLASSEXW`, and
`MSG` with its `POINT` member. Separately admit the sample-authored `Vertex`
only when its nominal declaration has exactly three `float32` fields in
`X`/`Y`/`Z` order, with offsets 0/4/8, size/alignment 12/4; this is a
sample-specific catalog rule, not a catalog-owned SDK type or general
[Blittable contract](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations).
Use aligned inline local storage, materialized before address-taking under the
accepted [lifetime and access rules](../specs/lifetimes.md#definitions-and-notation);
no hidden allocation or pinning. Keep representations compilation-local, with private fields and
checked helpers, rather than adopting the sketch's public fields or widening
the separate public fixed-layout proposal. Permit only their reviewed scalar,
nested-carrier, nominal handle and qualified-pointer fields. Windows x64
`nint`/`nuint` carriers are signed/unsigned eight-byte values; `float32`
storage is four-byte IEEE binary32. SDK enums/flags, Win32 `BOOL` and HRESULT
use their reviewed 32-bit carriers without becoming Terrum `bool` or
interchangeable nominal values. Opaque-handle ownership follows the accepted
[lifetime contracts](../specs/lifetimes.md#definitions-and-notation). This
does not widen the existing `HWND.NoOwner` gate.
Pass descriptor aggregates only by address, never by aggregate value; scalar
and handle values use their exact reviewed native parameter forms. Ordinary
checked copies retain the accepted
[value and dependency contract](../specs/lifetimes.md#passing-and-storage-access).

#### Initialization and zero templates

**Summary:** The sketch initializes most descriptors with `*.Zero`, then
sets selected fields. Zero representation is not necessarily an API-ready
descriptor or a valid value of an arbitrary Terrum type.

**Recommendation:** Make `*.Zero` a reviewed constructor only for the closed
native carriers whose every field admits its documented zero/reset value,
including explicitly nullable pointer/handle slots. Check that eligibility
transitively; a required non-null pointer, owner, managed reference or other
nonzero invariant excludes the carrier. SDK enum/flag reset bits need an
explicit carrier-validity rule, not an invented ordinary enum case. Initialize
all fields, including untouched nested/reserved fields, before publication.
The accepted [layout contract](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations)
leaves ordinary padding outside source value semantics; reserved or padding
bytes a native operation observes need an explicit boundary initialization
contract, not inferred permission to read object bytes.
Required call inputs such as `cbSize`, sample count and buffer size still
need their API-specific checks. Address formation follows the accepted
[initialization and access contracts](../specs/lifetimes.md#passing-and-storage-access);
for this profile, use `.Zero` or complete construction before a writable
native call. Record each writable import's validity and write guarantees on
success and failure; do not infer complete initialization from a return code,
silently fabricate an output, or promise rollback of native writes.

#### Field access and update

**Summary:** Nested updates such as `swapDescription.BufferDesc.Width = width`
must affect the original descriptor, not a temporary copy from an accessor.

**Recommendation:** Lower the closed carriers' checked field helpers to typed
place paths with catalogued offsets, evaluating receivers/indices once under
the accepted [place and access rules](../specs/lifetimes.md#passing-and-storage-access).
Nested updates must affect original storage and preserve other fields.
Replacing a pointer field must enforce its catalogued extent and
[backing dependencies](../specs/lifetimes.md#three-independent-dependencies).

#### Fixed inline-array storage

**Summary:** The sketch needs one feature level, three vertices and four
clear-color components, not a general collection or generic array library.

**Recommendation:** Initially admit only
`InlineArray<D3D_FEATURE_LEVEL, 1>`, `InlineArray<Vertex, 3>` and
`InlineArray<float32, 4>`. Require a positive compile-time extent, checked
target-representable storage size, and an initializer supplying every element
in source order before publication; no implicit default-filled tail. Elements
occupy contiguous receiver-owned inline storage with the element's validated
alignment and padded size as stride: expected sizes are 4, 36 and 16 bytes.
Checked indexing forms an element place; `&array[0]` is bounded by array
backing and readable/writable element count, not an unbounded pointer. Reject
invalid indices before address calculation. Existing
[copy and loan rules](../specs/lifetimes.md#passing-and-storage-access) govern
dependencies and outstanding element loans. Do not add resizing, managed-array
addresses, general array decay or pointer arithmetic.

#### Synchronous pointer loans and embedded pointers

**Summary:** `initialData.pSysMem = &vertices[0]` stores a stack address before
`CreateBuffer`; checking only a direct pointer argument would miss that
dependency. Descriptor pointers and literal fields likewise need more than
the numerical address or a raw `ptr<T>` signature.

**Recommendation:** Extend the immutable binding catalog with exact reviewed
parameter types, read/write authority, byte/element extents, count/size
relationships, nullability and synchronous no-retention contracts. Binding
and mandatory verification must preserve these facts through lowering and
check nested pointer fields as well as direct arguments. Permit
`&local`, eligible field/element addresses, and bounded pointer storage in
these local descriptors only under the accepted
[pointer, access and lifetime rules](../specs/lifetimes.md#definitions-and-notation)
through every use, including failure/unwind.

Allow only the reviewed readonly `Vertex`-data-to-`ptr<void>` erasure for
`pSysMem`, preserving its catalogued access and 36-byte backing extent;
`ByteWidth` must not exceed that readable extent. Descriptor copies and
forwarding through sample-local synchronous helpers must preserve the same
checked dependencies and no-escape contract; uncontracted forwarding rejects.
The accepted [loan and backing rules](../specs/lifetimes.md#three-independent-dependencies)
govern the last dependent use. The eventual exact `CreateBuffer` binding must
copy any consumed data before returning and retain no caller address on any
outcome; Stage 4 still owns the COM call.
No-escape covers addresses, not the API's independently specified resource
retention. A foreign provider's no-retention promise is a reviewed catalog
assertion, not proof supplied by an ABI annotation or fixture.

#### Stable literal pointer fields

**Summary:** The sample passes UTF-8 entry-point/target literals directly to
`D3DCompileFromFile` and stores a UTF-8 semantic-name literal in an input
descriptor field. The provisional UTF-16 gate covers neither UTF-8 use.

**Recommendation:** Admit readonly ASCII-compatible UTF-8-NUL literals only at the exact
`D3DCompileFromFile` entry-point/target argument positions and the
`D3D11_INPUT_ELEMENT_DESC.SemanticName` field used by this sample. Validate
encoding, extent, final terminator and absence of embedded NUL, retaining the
existing 8 MiB aggregate encoded-data budget and immutable executable-image
storage. Keep direct-argument validation distinct from the checked
field-store/dependency path; neither gate implies general UTF-8 string
marshalling. Non-ASCII narrow literals remain outside this sample-specific
slice. No writable projection or dynamic string marshalling is implied.
`WNDCLASSEXW` callback-address creation/registration and `MSG` callback dispatch
remain Stage 3; COM receivers, pointer outputs and ownership remain Stage 4.
Their layout admission or zero slots authorize neither calls nor callbacks.

#### DX11 SDK layout evidence and implementation gate

**Summary:** The sample's native descriptor and message structures now have
an x64 C layout probe compiled against the Windows SDK selected by the test
machine's `vcvars64.bat`. It asserts size, alignment and accessed field
offsets for `DXGI_MODE_DESC` (28/4), `DXGI_SAMPLE_DESC` (8/4),
`DXGI_SWAP_CHAIN_DESC` (72/8), `D3D11_BUFFER_DESC` (24/4),
`D3D11_SUBRESOURCE_DATA` (16/8), `D3D11_INPUT_ELEMENT_DESC` (32/8),
`D3D11_VIEWPORT` (24/4), `RECT` (16/4), `WNDCLASSEXW` (80/8) and `MSG`
(48/8). The probe passed against Windows SDK 10.0.28000.0 on the current
host.

**Recommendation:** Retain
[`dx11-sdk-layout-probe.c`](../../tests/native/dx11-sdk-layout-probe.c) as the
sample-specific ABI oracle, recording target and SDK version. Before admitting
each carrier, compare independently computed Terrum size/alignment, nested
offsets and array stride with SDK/C evidence; extend evidence for
`DXGI_RATIONAL`, `POINT`, `Vertex` and fields not asserted by the current
probe rather than assuming coverage. Reject mismatches and unsupported
layouts before emission. Then gate Terrum-produced initialized descriptors,
nested updates, arrays and readonly/writable pointer calls against independent
C readers/writers with extent canaries, including register/stack positions and
failure outcomes. Add negative checks for invalid zero eligibility, vacant
reads/addresses, extent violations, authority upgrades and direct/embedded
escapes, including descriptor copies. Keep unsupported persistence and JIT
paths rejecting the slice. Matching SDK layout alone establishes none of
these Terrum behaviors, a [Blittable commitment](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations)
or proposal approval.

3. **One static window callback.** Support the sample's exact noncapturing
   `WNDPROC` signature, calling convention and native-entry address formation,
   not general delegates or closures. Retain code and data lifetime through
   final window destruction and class unregistration, including synchronous
   reentry during creation and teardown. Satisfy the accepted
   [callback fault-containment rule](../specs/language.md#accepted-direction);
   private checked-failure catches are not a general callback fault barrier.
   Gate with a create/paint/close window and exactly-once, creating-thread
   cleanup.
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
literals in immutable executable-image storage. The producer validates
encoding, initialized extent and the final terminator; accepted
[lifetime and access rules](../specs/lifetimes.md#definitions-and-notation)
remain in force. General pointer storage, arithmetic, dereference,
managed-storage addresses and propagation remain unsupported.

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

**Current status:** This proposal remains unapproved. An earlier single-key
catalog prototype included a sample-specific `HWND.NoOwner` gate and a
`MessageBoxW` smoke test; that compiler-owned API policy has since been
retired. The production Windows x64 import catalog is empty. Current
provisional source-authored direct imports and their generic pointer metadata
are described separately in the
[TRRM native interop proposal](trrm-native-interop-metadata.md), and are not
approval of this proposal's catalog or handle forms. The earlier host-specific
smoke-test result is historical evidence, not current compiler support.

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
cross-version managed ABI. Raw-pointer validity and backing remain governed
by the accepted [lifetime and access contracts](../specs/lifetimes.md#definitions-and-notation).
No source grant, native package, or existing private native ABI silently
authorizes these features.

The sample does not move Terrum's scalar frontend beyond the hosted verifier
and emitter. A successful build is not evidence that the self-host gate passes.
