# Native imports and COM metadata in TRRM

**Status: UNAPPROVED proposal for review.** This proposal adds no records to
the canonical TRRM design and does not approve current implementation
experiments. A provisional direct native-import slice exists in the branch;
its unapproved wire assignments and support limits are recorded below. This
proposal describes the broader generic metadata surface needed to describe
native DLL imports and COM calls in a Terrum dependency, so the compiler can
consume the dependency's declarations instead of owning an API catalog.

## Goal

A Terrum library should be able to declare a native boundary, compile that
declaration and its contract into TRRM, and expose it through an ordinary
Terrum dependency. A consumer should bind the selected dependency and obtain
the native binding and COM facts from its metadata. The compiler should then
validate and lower those facts using generic target rules.

This is a format proposal, not a decision about the broader native-package
architecture. It does not change `.trmn` or `.trng`, and it does not add a
separate C# profile, compiler registry, or DX11-specific path.

These records describe the selected dependency's declarations, so TRRM should
be their source of truth. Native package formats may carry executable images
and deployment data, but they should not be the only place a consumer can
learn an imported method's signature or COM contract.

## Current gap

TRRM already represents Terrum modules, types, fields, target layouts,
callable signatures, parameter modes, semantic contracts, and
producer/key imports. Its existing `Import` row selects a Terrum producer and
export; it does not identify an operating-system DLL or native export.

An earlier native-catalog prototype filled that gap out of band, including
DX11 COM and sample behavior; that API-specific catalog policy has been
retired. The production Windows x64 import catalog is empty. The branch now
has a provisional source-authored direct-import form, but its experimental
TRRM assignments and incomplete dependency paths are not canonical metadata.
This proposal addresses portable dependency consumption of authored native
declarations and generic COM contracts; it does not preserve catalog-only
convenience behavior or add API-specific compiler policy.

## Unsafe boundary and representation

A native import or COM method is a call across the Terrum boundary into code
whose implementation and memory behavior are not checked by Terrum. A
declaration that is callable from non-Terrum code crosses the same boundary
in the other direction. These raw declarations are caller-unsafe even when
their signatures contain only scalar values: their `MethodDef` must carry
the existing `Effects.RequiresUnsafeCaller` contract. A caller must satisfy
that obligation in an explicit `unsafe { ... }` block or `unsafe(expr)`;
declaring the caller `unsafe` propagates its obligation but does not make its
body an implicit unsafe scope. `NoThrow` and other effects are separate
promises; they do not remove the unsafe obligation.

`unsafe` is an authored trust contract: it marks operations whose safety the
compiler cannot establish and places responsibility on the author and caller.
An explicit unsafe block or expression can contain that obligation inside an
authored wrapper; it does not prove the wrapper's assumptions or the foreign
provider's behavior. Unsafe code may be used to work around initialization
analysis, a compiler limitation, or an optimization decision, and may also
contain an intentional bug; the language does not judge those choices.
However, `unsafe` need not authorize every possible escape: the language can
limit which otherwise-dangerous operations it admits while still supporting
common boundaries such as FFI. For admitted unsafe operations, the compiler
should preserve source intent and may report an obvious contradiction as a
suppressible warning, but should not reject code merely because a fact beyond
the language's verification boundary cannot be proven. Safe code remains
subject to the compiler's ordinary verification rules and blocking
diagnostics.

Every by-value scalar/aggregate and every data region exposed through a
pointer must have the selected target's fixed `Blittable` representation.
Opaque native handles, including COM interface pointers, use their explicit
handle/interface contract rather than pretending the referenced object is a
blittable Terrum value. The raw declaration spells the actual ABI shape:
native-width scalar types, pointers, and explicitly laid-out values.
`NativeParameter` describes pointer access and retention, but it does not
grant safety or prove that the foreign implementation honors those claims.
`ref<T>` is not a pin: ordinary reference formation acquires no address
stability. A C pointer parameter is represented as `ptr<T>` in the raw FFI
signature. The caller may form that pointer from stable stack/native storage,
or from movable managed storage only under an explicit live `pin ref<T>`
dependency. `ptr<T>` itself does not root or pin its referent.
Any temporary required by the target ABI for a by-value `Blittable` aggregate
is ordinary ABI lowering, not a general-purpose marshalling or pinning step.

This proposal adds no built-in marshaller. It does not implicitly convert
strings, managed references, or arrays; copy managed values to temporary
native buffers; root or pin objects; or otherwise extend their lifetimes.
Any buffer construction, encoding, pinning facility, or safe wrapper must be
ordinary explicitly authored Terrum code with its own checked contract.
Blittability fixes representation; it does not make an unsafe call safe.
An address may refer directly to stack storage only for the duration and
access permitted by its explicit loan contract; an import with a retention
contract cannot silently extend that storage's lifetime.

An authored wrapper may contain a raw call in an explicit unsafe block and
expose a safe API only when it establishes the full safety contract. For
example, a wrapper over `QueryPerformanceCounter` can check the native
success result before exposing its output; a COM owner wrapper can keep an
owned interface pointer private and release it during cleanup. Such wrappers
do not change the unsafe status of the imported declarations themselves.
The accepted [caller-unsafe rules](../specs/grammar-spec.md#53-caller-unsafe-declarations-and-explicit-unsafe-scopes)
and [Blittable contract](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations)
remain authoritative.

The intended model is direct blittable FFI, closer to .NET interop with
runtime marshalling disabled and a generated `LibraryImport` stub than to
classic runtime-marshalled `DllImport`.

### `ref<T>`, `pin ref<T>`, and `ptr<T>` at the boundary

These forms overlap in how callers can reach storage, but they are not aliases:

| Form | Contract | Native-boundary role |
|---|---|---|
| `ref<T>` | Checked reference descriptor; ordinary formation acquires no pin and does not promise a stable raw address. | Not the C pointer type. |
| `pin ref<T>` | Checked reference plus an explicit live pin/root dependency that keeps eligible backing address-stable for that dependency's lifetime. | Can convert to the matching `ptr<T>` while the pin remains live. The conversion acquires no pin. |
| `ptr<T>` | Explicit raw address; it carries no root, ownership, or pin by itself. Dereference requires unsafe authorization and valid lifetime/provenance. | The direct representation for a C `T*`; `ptr<ptr<T>>` represents `T**`. |
| `init ref<T>` | Initialization-destination parameter mode for a Terrum `T` value, not a raw pointer or a type qualifier inside `T`. | Appropriate on a safe Terrum wrapper. Do not use it in place of the raw FFI pointer parameter. |

Thus `ptr<T>` is not `pin ref<T>`: a pointer may refer to stack or native
storage without a GC pin, while a pointer into movable managed storage
requires an explicit live pin or other independently stable backing. The QPC
wrapper uses `init ref<int64>` only for its Terrum-facing output; the raw
import accepts `write ptr<int64>` and receives the address of an initialized
stack local. Similarly, the raw DXGI acquisition takes `ptr<Guid>` and
`write ptr<ptr<void>>`; a wrapper forms those addresses from local storage
inside an unsafe scope, then adopts the returned interface pointer after
checking the requested IID.

`init ref<T>` is currently a parameter mode with call-site spelling
`init value`; it is not a first-class `init T` type qualifier. This proposal
therefore keeps `init ref<T>` in safe wrapper signatures and uses
`NativeParameter`, `NativeOutput`, and `NativeComAcquisition` metadata for raw
pointer access, conditional validity, and ownership. Whether the language
should add a form such as `ref<init T>` or `ptr<init T>` is a separate type
model question, not an implied part of this TRRM extension.

An `init var` call-site argument is only an initialization destination for
the wrapper's parameter. If pointer-valued initialization destinations are
admitted, `init ref<ptr<T>>` initializes the pointer cell only; it does not
pin the pointee or confer ownership. Whether that destination form should be
admitted, and its required validity/ownership contract, remains open.

The proposed `NativeParameter` access field can distinguish simple readonly,
writable, and read/write pointer use; it does not settle a complete C/C++
const-qualification mapping. In particular, `T* const`'s top-level const is
callee-local and does not change the ABI signature, while `write ptr<T>` and
`ptr<write T>` qualify different levels of a Terrum type. A complete
per-indirection mapping and its relationship to `[In]`, `[Out]`, and
`[In, Out]` remain open; do not infer those contracts from `ptr<T>` alone.
These distinctions follow the current [reference and pointer rules](../specs/language/references.md)
and [initialization-loan syntax](../specs/grammar-spec.md#32-types-parameters-and-arguments).

## Proposed metadata

Reuse existing `Module`, `TypeDef`, `FieldDef`, `Layout`, `MethodDef`,
`MethodSig`, `Param`, `Import`, `Export`, and `Contract` records wherever they
already express the fact. Add one native binding table and a small set of
generic COM contract clauses.

### Native DLL binding

Add a `NativeImport` table. Each row associates a bodyless external `MethodDef`
with one target-resolved library and export:

| Field | Meaning |
|---|---|
| `methodDef` | The external method declaration; its existing `MethodSig` and `Param` rows are authoritative for the signature. |
| `libraryName` | A library identity, not a path. |
| `exportName` | The exact named export. |
| `resolutionPolicy` | A target-defined search/deployment policy; it does not grant arbitrary path loading. |

The current implementation experiment uses a provisional section kind
`0x0021` with fixed 16-byte rows: `methodDef`, `libraryName`, `exportName`,
and a 32-bit resolution policy (`1` for the selected Windows System32 policy).
These values are not accepted TRRM assignments and remain subject to
end-to-end review. The implementation is limited to named exports and this
single policy.

This row is distinct from `Import`: a Terrum `Import` resolves a dependency
declaration; `NativeImport` binds the selected declaration to an OS symbol.
The library's exported method has both its ordinary signature/contract and
its native binding row. A consumer imports that method through the ordinary
Terrum dependency mechanism. The selected provider's metadata supplies the
native binding when the consumer is lowered.

The module's target requirement and the existing signature/layout records
determine the ABI. Do not serialize register assignments, stack offsets, or
compiler-specific ABI strings in the binding row. A native import record is
declaration metadata, not proof that the foreign DLL implements the declared
contract.

### COM interfaces and calls

Add these contract clauses to the existing `Contract` table:

| Clause | Owner | Proposed payload and role |
|---|---|---|
| `NativeComInterface` | `TypeDef` | Interface IID and optional immediate base-interface `TypeDef`. |
| `NativeComMethod` | `MethodDef` | Explicit vtable slot and any native reference effect required by ownership checking. The method's existing signature describes the native call; the owning `TypeDef` identifies the interface. |
| `NativeComOwner` | `TypeDef` | The COM interface represented by this unique owner type. Reuse its existing layout and `Cleanup` contract for storage and release behavior. |
| `NativeParameter` | `MethodDef` | For an address-passed parameter, its read/write/inout access, retention rule, and any required extent not already determined by its type. |
| `NativeOutput` | `MethodDef` | Output parameter ordinal, result condition for a valid initialized value, and the value's failure state. |
| `NativeComAcquisition` | `MethodDef` | Output ordinal and resulting owner `TypeDef`, with references to the foreign acquisition source and interface-selection condition; records whether failure may still write an owned output. It composes with that method's output-state contract. |

Payload widths and enum assignments are intentionally unassigned in this
proposal. The reader must validate all references, unique slots, interface
inheritance, signature arity, unique parameter ordinals, output ordinals,
owner/interface compatibility, and failure cleanup requirements before
accepting the native contract. A `NativeOutput` must refer to a writable
parameter and a result condition expressible for that method's result type.
A `NativeComAcquisition` must refer to a declared output-state contract and
an owner for the declared interface. Every `MethodDef` carrying `NativeImport` or
`NativeComMethod` metadata must also carry `Effects.RequiresUnsafeCaller`;
readers must reject a missing unsafe obligation rather than infer that a
scalar-only signature is safe. A typed acquisition records the wrapper author's output/interface
correlation; it is an authored trust claim, not proof that foreign code
returned an object of that interface. For a selector API, authored code can
pass the intended IID explicitly (for example, `typeof(T).Guid` for `riid`),
but the author remains responsible for the foreign provider's behavior. The
compiler validates the metadata's structure and preserves unsafe
obligations; it may warn about an obvious source-level mismatch, but does not
silently reinterpret a failed proof as undefined behavior or require proof of
the provider's behavior to contain unsafety in an authored wrapper. A generic
`void**` output from a call with a caller-selected `REFIID` is not by itself
a compiler-proven typed interface result.
Unknown required clauses must reject; readers must not silently ignore
unsupported interop semantics.

For the current native-import/output experiment only, `NativeOutput` uses
provisional Contract kind `6` with a 16-byte payload: 32-bit parameter
ordinal, result-condition code, failure-disposition code, and a reserved
zero word. The source experiment defaults integer outputs to a nonzero result;
it also provisionally accepts `NativeOutput(ordinal, NonNegative)` for signed
nonnegative status results such as HRESULT success, using result-condition
code `3`. Neither the source form nor its wire code is standard approval. No
wire encoding is approved for the COM clauses. A structural reader experiment provisionally
uses Contract kinds `7` through `11`: kind `7` for `NativeComInterface`
(16-byte big-endian IID and base TypeDef row), kind `8` for `NativeComMethod`
(vtable slot and effect mask),
kind `9` for `NativeComOwner` (interface TypeDef row), kind `10` for
`NativeParameter` (ordinal, access, retention, and extent), and kind `11` for
`NativeComAcquisition` (wrapper output ordinal, owner TypeDef, source method,
source output ordinal, and failure disposition). These payload assignments
are implementation experiments only. Source-authored COM metadata binding
and in-compilation verification are implemented provisionally. The structural
reader validates clause shapes and the owning module's feature assignment;
semantic producer serialization, dependency reconstruction, and consumer-side
lowering remain unsupported. The metadata is an authored trust contract and
does not prove a foreign provider returned the declared interface.

For a branch-local aggregate-pointer experiment, use provisional Feature ID
`4` (`NativeBlittableLayouts`, revision `1`) and Contract kind `12`. Kind `12`
is owned by a `TypeDef` and has one `u32` payload: the explicit
`targetRequirement`, currently limited to Windows x64 requirement `1`. The
referencing `NativeParameter` signature uses `ptr<TypeDef>`; its existing
clause carries access, retention, and extent. The marked `TypeDef` must be a
non-unique struct with `Sequential` or `Transparent` layout, a matching
`Layout` row with default packing and zero flags, and recursively marked
value-struct fields. The initial executable subset accepts scalar fields and
nested marked structs. Pointer and function-pointer fields are excluded
because the current bound field representation does not retain their complete
type identity. Consumers must recompute field offsets, size, and alignment
and reject mismatched marker, feature, or layout facts; neither layout values
nor the target requirement alone imply the commitment.

Feature ID `4` and Contract kind `12` are temporary branch assignments, not
canonical `(1, 0)` assignments. They do not change the normative
specification's explicit statement that `(1, 0)` has no `Blittable` marker.
The experiment is limited to declaration-only native-import dependencies;
it does not add aggregate Body storage, by-value aggregate ABI, or
aggregate-output validity. The proposal remains unapproved and these choices
must be reconciled with a future assigned schema before any conformance claim.

`NativeParameter` and `NativeOutput` are needed because a pointer type alone
does not say whether the callee reads, writes, or retains the address, and a
signature alone does not say when an output becomes valid.

Use existing `TypeDef`/`FieldDef`/`Layout` records for native structure
declarations. Those rows describe physical representation, not API-specific
value constraints. Field requirements, backing extents, and fixed-array
storage rules should be checked by ordinary Terrum wrappers or a separately
specified generic storage contract; do not add DX11-specific layout rows.
Use existing callable signatures and semantic effect/cleanup contracts where
they already express the requirement. Do not add SDK type names or DX11
method names to the format.

### Keep API convenience in Terrum source

Native declarations should expose the exact native call shape. When an API
needs hidden constants, reordered arguments, aggregate-address adaptation,
buffer accessors, or a more convenient owner-facing method, express that
adaptation as an ordinary Terrum wrapper in the interop library. The initial
metadata proposal does not persist catalog-authored argument recipes or
sample policy. This keeps the compiler responsible for generic import and
vtable lowering, not for synthesizing an API's wrapper layer.

## Worked examples

The source examples use ordinary Terrum declarations and call syntax. The
tables show proposed TRRM metadata rows; the source syntax for authoring those
rows is still open. Each library method has its ordinary Terrum signature and
export contract. Native and COM records add only facts that cannot be
recovered from that signature.

### `QueryPerformanceCounter`

The Windows API is a `BOOL` function with one output pointer to a signed
64-bit `LARGE_INTEGER`. Its raw FFI signature uses `write ptr<int64>`:
`init ref<T>` is a checked Terrum destination descriptor, not the native
pointer type.

**Interop library declaration**

```terrum
namespace Terrum.Interop.Windows;

[NoThrow]
public extern unsafe int32 QueryPerformanceCounter(write ptr<int64> performanceCount);
```

The bodyless `extern unsafe` declaration is Terrum source syntax. Its
`Kernel32.dll!QueryPerformanceCounter` binding and pointer/output contracts
are separate TRRM metadata rows; the source syntax that authors those rows
remains open in this proposal.

**Consumer use**

```terrum
using Terrum.Interop.Windows;

public int32 Main() {
    if (!TryGetPerformanceCounter(init var ticks)) {
        return 1;
    }
    ObserveCounter(ticks);
    return 0;
}

[NoThrow]
internal void ObserveCounter(int64 ticks) {}
```

An authored safe wrapper can contain the unsafe call and publish its own
result/output contract:

```terrum
public bool TryGetPerformanceCounter(init ref<int64> ticks) {
    mut int64 nativeTicks = 0;
    unsafe {
        int32 succeeded = QueryPerformanceCounter(&nativeTicks);
        if (succeeded == 0) {
            return false;
        }
    }
    *ticks = nativeTicks;
    return true;
}
```

That wrapper must state that `ticks` is initialized on `true` and unavailable
on `false`. It passes the address of an initialized stack local for the
synchronous call, then copies the successful scalar into the safe output
destination. A stack local is stable for this call; a pointer into movable
managed storage would instead require an explicit pin or separately stable
native storage. The compiler inserts neither.

The exported declaration produces or reuses these TRRM records:

| Record | Example value |
|---|---|
| `MethodDef` / `MethodSig` / `Param` | `int32 QueryPerformanceCounter(write ptr<int64>)`; parameter 0 is a writable raw pointer to one `int64`. |
| `NativeImport` | Method `QueryPerformanceCounter` → `Kernel32.dll!QueryPerformanceCounter`, resolved by the selected Windows system-library policy. |
| `Effects` | `RequiresUnsafeCaller` and `NoThrow`. |
| `NativeParameter` | Parameter 0 is writable and non-retained; its pointee is one `Blittable` `int64`. |
| `NativeOutput` | Parameter 0 is valid when the `int32` result is nonzero. On zero, its state is unspecified and the caller must not read it. |
| `OutputState` | On the safe `TryGetPerformanceCounter` wrapper, `ticks` is initialized on `true` and vacant on `false`. |

The Windows x64 backend derives the pointer argument and 32-bit result
classification from the target and signature. TRRM does not store `RCX`,
`EAX`, or another compiler's register/stack plan. This example also shows why
an output declaration needs more than a `MethodSig`: the signature identifies
the return type and output destination, while `NativeOutput` records that the
destination is usable only when `BOOL` is nonzero.

### `IDXGIDebug`

`DXGIGetDebugInterface1` is the native DLL import; `IDXGIDebug` itself is a
COM interface. The interface inherits the three `IUnknown` slots and declares
`ReportLiveObjects` at slot 3.

**Interop library declarations**

The source shows only ordinary external and interface signatures. DLL,
interface-IID, slot, parameter-access, output, and acquisition details are
separate TRRM records below; their source authoring syntax is deliberately
not invented here.

```terrum
namespace Terrum.Interop.Dxgi;

[NoThrow]
public extern unsafe int32 DXGIGetDebugInterface1(
    uint32 flags,
    ptr<Guid> riid,
    write ptr<ptr<void>> ppDebug);

public interface IDXGIDebug : IUnknown {
    [NoThrow]
    public unsafe int32 ReportLiveObjects(Guid apiId, int32 flags);
}

public unique struct DxgiDebugOwner {
    private ptr<IDXGIDebug> Pointer;

    public unsafe DxgiDebugOwner(ptr<IDXGIDebug> pointer) {
        Pointer = pointer;
    }

    [NoThrow] [Cleanup] public void Close() {
        unsafe {
            _ = Pointer->Release();
        }
    }

    [NoThrow] public int32 ReportLiveObjects(Guid apiId, int32 flags) {
        unsafe {
            return Pointer->ReportLiveObjects(apiId, flags);
        }
    }
}
```

**Consumer call**

```terrum
using Terrum.Interop.Dxgi;

public int32 ReportAllLiveObjects(DxgiDebugOwner debug, Guid apiId, int32 flags) {
    return debug.ReportLiveObjects(apiId, flags);
}
```

`IUnknown` is supplied by the shared COM declarations: `QueryInterface`,
`AddRef`, and `Release` occupy slots 0, 1, and 2. The interop library also
declares `Guid` with the Windows layout (16 bytes, 4-byte alignment).
`DXGI_DEBUG_ALL` is a GUID and `DXGI_DEBUG_RLO_ALL` is the flag value `0x7`;
they are passed as ordinary Blittable values. A library can expose them as
type-owned values rather than relying on namespace-level constants.
`DXGIGetDebugInterface1` accepts any `REFIID` and returns an untyped `void**`;
its raw import cannot promise that every output is `IDXGIDebug*`. An ordinary
Terrum factory calls it in an explicit unsafe scope with the address of a
local `Guid` and a mutable, initialized local `void*` cell. The factory fixes
the requested IID to `IID_IDXGIDebug`, checks the HRESULT, releases a
possibly written failure output, and transfers a successful pointer into
`DxgiDebugOwner`; the compiler does not synthesize that wrapper. These locals
have stable stack storage for the synchronous call, so no GC pinning is
needed. The owner type itself stays on the Terrum side of the boundary; only
its opaque interface pointer is passed to COM.

| Record | Example value |
|---|---|
| `NativeImport` | `DXGIGetDebugInterface1` → `Dxgi.dll!DXGIGetDebugInterface1`, Windows system-library policy. |
| `MethodSig` / `Param` | `int32(uint32, ptr<Guid>, write ptr<ptr<void>>)`; parameter 1 is `REFIID`, parameter 2 is writable `void**`. |
| `Effects` | `RequiresUnsafeCaller` and `NoThrow` on the raw import and COM method. |
| `NativeParameter` | Parameter 1 is read-only and non-retained; parameter 2 is writable and non-retained. |
| Raw import `NativeOutput` | Parameter 2 is valid when the HRESULT is nonnegative. Conservatively, treat it as possibly written on failure. |
| Wrapper `MethodSig` / `OutputState` | `bool TryGetDxgiDebug(init ref<DxgiDebugOwner>)`; its owner output is initialized on success and vacant on failure. |
| Wrapper `NativeComAcquisition` | The wrapper's output is an owned `DxgiDebugOwner`, acquired through `DXGIGetDebugInterface1` with `riid = IID_IDXGIDebug`; it releases any non-null raw failure output. |
| `NativeComInterface` | `IDXGIDebug` has IID `119E7452-DE9E-40FE-8806-88F90C12B441` and base `IUnknown`. |
| `NativeComMethod` | `ReportLiveObjects` is slot 3 with signature `int32(Guid, int32)`; `IUnknown.Release` is slot 2 and releases a reference. The `Guid` layout and target requirement let the backend derive aggregate passing. |
| `TypeDef` / `Layout` | `Guid` is 16 bytes, 4-byte aligned, and carries the target's `Blittable` commitment. |
| `NativeComOwner` / `Cleanup` | `DxgiDebugOwner` represents `IDXGIDebug`; its ordinary `Close` method calls inherited `IUnknown.Release` at slot 2. |

There is no DLL-import row for `ReportLiveObjects`: it is reached through the
declared COM vtable slot. The failure-write rule above is a conservative
interop declaration, not a claim that the API documents a non-null output on
failure. `DXGIGetDebugInterface1` may return `E_NOINTERFACE` when the Windows
SDK debug components are unavailable, and `IDXGIDebug` requires the DXGI debug
layer to be enabled. Those are runtime conditions handled by the Terrum
caller, not facts that change the DLL identity or vtable metadata.

API references:

- Microsoft Learn: [`QueryPerformanceCounter`](https://learn.microsoft.com/en-us/windows/win32/api/profileapi/nf-profileapi-queryperformancecounter)
- Microsoft Learn: [`DXGIGetDebugInterface1`](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_3/nf-dxgi1_3-dxgigetdebuginterface1)
- Microsoft Learn: [`IDXGIDebug`](https://learn.microsoft.com/en-us/windows/win32/api/dxgidebug/nn-dxgidebug-idxgidebug) and [`ReportLiveObjects`](https://learn.microsoft.com/en-us/windows/win32/api/dxgidebug/nf-dxgidebug-idxgidebug-reportliveobjects)
- SDK header mirror: [`dxgidebug.h` interface declaration](https://github.com/apitrace/dxsdk/blob/master/Include/dxgidebug.h)

## Data flow

```text
Terrum interop declarations
        -> TRRM dependency metadata
        -> ordinary dependency binding
        -> generic native-import / COM validation and target lowering
        -> PE imports and vtable dispatch
```

There is no independent DX11 catalog or runtime profile-selection mechanism
in this flow. The declarations and metadata are selected as part of the
Terrum dependency contract; target-specific ABI classification remains in
the backend.

## Validation and evidence

The metadata writer should reject malformed or incomplete declarations.
Binding should check the consumer's expected Terrum signature and contracts
against the selected dependency before emission. Lowering should derive ABI
locations from the selected target and exact signature; COM calls should use
the declared interface and slot rather than method-name lookup.

Tests should include unrelated synthetic native libraries and COM interfaces,
including malformed bindings, duplicate/invalid slots, invalid ancestry,
signature mismatches, forged output owners, and invalid failure dispositions.
Independent C/C++ ABI probes remain evidence for the foreign declaration; a
TRRM record alone does not prove provider conformance.

## Non-goals and open review points

- This does not redesign `.trmn`, `.trng`, native deployment, or graph
  packaging.
- It does not define arbitrary DLL paths, plugin loading, or caller-selected
  binding catalogs.
- It does not add a C header importer, general marshalling, callbacks, or
  application-specific wrapper generation.
- Native callable exports and callbacks are outside this first slice, but
  their ABI-crossing signatures inherit the same caller-unsafe and Blittable
  requirements.
- It does not change accepted language, ABI, ownership, or lifetime
  semantics.
- Source syntax for authoring COM clauses remains open. The implementation
  experiment supports a named DLL/export declaration and result-correlated
  scalar or opaque `ptr<void>` output; it does not establish an accepted
  source-authoring contract or COM acquisition/ownership semantics.
- The current DX11 path also relies on fixed-array/storage-record constraints
  and mapped-buffer behavior. This proposal does not encode those as import
  metadata; review whether ordinary source wrappers and existing target
  layout contracts suffice, or whether a separate generic storage contract is
  required.
- Review whether the first slice needs explicit metadata for reference-count
  cloning beyond owned outputs and ordinary `Cleanup`, or whether that
  behavior should remain a Terrum wrapper.
- Exact table/clause assignments and the required-feature identifier remain
  unapproved. The implementation experiment provisionally uses section
  `0x0021` with 16-byte rows (`MethodDef`, library `String`, export `String`,
  System32 resolution policy `1`), `Contract` clause kind `6` with a
  16-byte payload (parameter ordinal, result condition, failure disposition,
  reserved zero), and Feature ID `2`, rule revision `1`, for Windows named
  native imports. The user-directed provisional Feature assignment preserves
  ID `1` for `ScalarInitOutput`; neither assignment is a canonical registry
  change.
- The separate structural COM reader experiment provisionally uses Contract
  kinds `7` through `11` and Feature ID `3`, rule revision `1`, as described
  above. These remain implementation experiments, not standard assignments.
  Source-authored COM binding and verification exist. A bounded semantic
  producer/consumer experiment now serializes and reconstructs interface IID/
  ancestry, vtable slot/effect, and `NativeParameter` metadata using clauses
  `7`, `8`, and `10`; a consumer-authored fixed-interface owner/acquisition
  wrapper can bind against that reference and lower a vtable call. It does not
  yet persist/reconstruct owner/acquisition clauses `9` and `11`, allowing
  those wrappers to be supplied by a separate TRRM dependency, or establish
  foreign COM runtime behavior.
- The prototype represents `write ptr<T>` with the existing inline
  `TypeSpec` family encoding for `ptr<T>` (`familyCode=0x8C`, writable
  qualifier `0x01`, and the atomic code for `T`). This reuses the existing
  TypeSpec grammar and does not assign a new form.
- The prototype admits bodyless source-authored imports for named DLL
  exports under the Windows System32 policy, with directly Blittable scalar
  parameters and writable scalar pointer outputs. Each output is valid only
  for an `int32` or `uint32` nonzero result; its failure disposition is
  unspecified. The mandatory verifier still requires `Effects.RequiresUnsafeCaller`.
  Private semantic producer/native package paths now preserve and revalidate
  these direct-import and writable-output facts, and bind a consumer against
  the produced DLL. This does not establish JIT, module-set/graph,
  semantic TRRM COM dependency, COM acquisition, or runtime execution
  support. The proposal remains unapproved; these implementation paths do not
  assign canonical TRRM semantics or deployment behavior.
