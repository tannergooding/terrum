# Native import metadata in TRRM

**Status: UNAPPROVED proposal for review.** This proposal adds no records to
the canonical TRRM design and does not approve current implementation
experiments. A provisional direct native-import slice exists in the branch;
its unapproved wire assignments and support limits are recorded below. This
proposal describes provisional generic native-import metadata. Earlier
COM-specific interface, method, owner, acquisition, reference-count, and
cleanup clauses and worked examples are withdrawn; they are not required TRRM
gaps and must not be implemented from this proposal. COM ownership and
wrappers belong in authored Terrum code. Generic vtable metadata remains
unresolved and is outside this proposal. See
[Unsafe FFI and native vtable cleanup](unsafe-ffi-vtable-cleanup.md).

## Goal

A Terrum library should be able to declare a native boundary, compile that
declaration and its contract into TRRM, and expose it through an ordinary
Terrum dependency. A consumer should bind the selected dependency and obtain
the native binding and generic pointer/output facts from its metadata. The
compiler should then validate and lower those facts using generic target
rules.

This is a format proposal, not a decision about the broader native-package
architecture. It does not change `.trmn` or `.trng`, and it does not add a
separate C# profile, compiler registry, or DX11-specific path.

These records describe the selected dependency's declarations, so TRRM should
be their source of truth. Native package formats may carry executable images
and deployment data, but they should not be the only place a consumer can
learn an imported method's signature and native binding.

## Current gap

TRRM already represents Terrum modules, types, fields, target layouts,
callable signatures, parameter modes, semantic contracts, and
producer/key imports. Its existing `Import` row selects a Terrum producer and
export; it does not identify an operating-system DLL or native export.

An earlier native-catalog prototype filled that gap out of band, including
DX11 and sample behavior; that API-specific catalog policy has been retired.
The production Windows x64 import catalog is empty. The branch now has a
provisional source-authored direct-import form, but its experimental TRRM
assignments and incomplete dependency paths are not canonical metadata. This
proposal addresses dependency consumption of authored native declarations;
it does not preserve catalog-only convenience behavior or add API-specific
compiler policy. Generic vtable calls exist in source and private-module
prototypes, but their standard TRRM representation is outside this proposal
and remains unresolved.

## Unsafe boundary and representation

A native import is a call across the Terrum boundary into code whose
implementation and memory behavior are not checked by Terrum. A declaration
that is callable from non-Terrum code crosses the same boundary in the other
direction. These raw declarations are caller-unsafe even when their
signatures contain only scalar values: their `MethodDef` must carry the
existing `Effects.RequiresUnsafeCaller` contract. A caller must satisfy that
obligation in an explicit `unsafe { ... }` block or `unsafe(expr)`; declaring
the caller `unsafe` propagates its obligation but does not make its body an
implicit unsafe scope. `NoThrow` and other effects are separate promises;
they do not remove the unsafe obligation.

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
Opaque native handles are represented by their ABI pointer values; that does
not make the referenced object a blittable Terrum value or establish its
identity, ownership, or lifetime. The raw declaration spells the actual ABI
shape: native-width scalar types, pointers, and explicitly laid-out values.
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
success result before exposing its output. Such wrappers do not change the
unsafe status of the imported declarations themselves.
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
stack local. A pointer-to-pointer output such as `write ptr<ptr<void>>`
describes the ABI shape only; it does not establish the returned pointer's
identity, ownership, or lifetime.

`init ref<T>` is currently a parameter mode with call-site spelling
`init value`; it is not a first-class `init T` type qualifier. This proposal
therefore keeps `init ref<T>` in safe wrapper signatures and uses
`NativeParameter` and `NativeOutput` metadata for raw pointer access and
conditional validity. Any ownership remains an authored wrapper contract;
these annotations do not supply it. Whether the language should add a form
such as `ref<init T>` or `ptr<init T>` is a separate type model question, not
an implied part of this TRRM extension.

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
`MethodSig`, `Param`, `Import`, `Export`, and `Contract` records wherever
they already express the fact. Add one native binding table and generic
pointer/output clauses for native imports. This proposal does not define
standard vtable metadata.

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

### Generic vtable calls remain unresolved

The compiler's source/private-module prototype supports numeric-slot vtable
calls, but that does not establish standard TRRM support. Any future
vtable-to-TRRM mapping needs a separately reviewed contract for the exact
target ABI, explicit receiver, slot, and method signature. The source spelling
and wire representation remain open; this proposal assigns no vtable records
or wrapper recipes.

For the current native-import/output experiment only, `NativeOutput` uses
provisional Contract kind `6` with a 16-byte payload: 32-bit parameter
ordinal, result-condition code, failure-disposition code, and a reserved
zero word. The source experiment defaults integer outputs to a nonzero result;
it also provisionally accepts `NativeOutput(ordinal, NonNegative)` for signed
nonnegative status results such as HRESULT success, using result-condition
code `3`. Neither the source form nor its wire code is standard approval.
`NativeParameter` uses provisional Contract kind `10` for native-import
pointer access, retention, and extent facts; it does not define vtable
metadata.

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
sample policy. This keeps the compiler responsible for generic native-import lowering, not
for synthesizing an API's wrapper layer.

## Worked examples

The source examples use ordinary Terrum declarations and call syntax. The
tables show proposed TRRM metadata rows; the source syntax for authoring those
rows is still open. Each library method has its ordinary Terrum signature and
export contract. Native-import records add only facts that cannot be
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

### Generic vtable metadata

This proposal makes no standard TRRM claim or worked example for vtable
dispatch. The maintained sample's interface declarations, slot choices,
pointer ownership, and cleanup are authored Terrum code. The source/private
numeric-slot prototype and private semantic-module revision 84 do not assign
standard TRRM records or define generic vtable metadata.

A generic vtable source or TRRM mapping remains a separate unresolved
contract.

- Microsoft Learn: [`QueryPerformanceCounter`](https://learn.microsoft.com/en-us/windows/win32/api/profileapi/nf-profileapi-queryperformancecounter)

## Data flow

```text
Terrum interop declarations
        -> TRRM dependency metadata
        -> ordinary dependency binding
        -> generic native-import validation and target lowering
        -> PE imports
```

There is no independent DX11 catalog or runtime profile-selection mechanism
in this flow. The declarations and metadata are selected as part of the
Terrum dependency contract; target-specific ABI classification remains in
the backend.

## Validation and evidence

The metadata writer should reject malformed or incomplete declarations.
Binding should check the consumer's expected Terrum signature and contracts
against the selected dependency before emission. Lowering should derive ABI
locations from the selected target and exact signature. Direct native imports
should use the exact declared export rather than compiler-owned API policy.

Tests should include unrelated synthetic native libraries and malformed
bindings, invalid pointer clauses, signature mismatches, and invalid output
conditions. Independent C/C++ ABI probes remain evidence for the foreign
declaration; a TRRM record alone does not prove provider conformance.

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
- Generic vtable source syntax and standard metadata remain unresolved and
  outside this direct-import proposal. Vtable calls, interface identities,
  ownership, acquisition, reference counting, and cleanup are not represented
  by the withdrawn clauses; API-specific behavior belongs in authored Terrum
  declarations and wrappers.
- Sample-specific storage, mapped-buffer, and cleanup contracts are not
  import metadata. This proposal does not add API-specific layout records or
  compiler-generated wrappers.
- Exact table/clause assignments and the required-feature identifier remain
  unapproved. The implementation experiment provisionally uses section
  `0x0021` with 16-byte rows (`MethodDef`, library `String`, export `String`,
  System32 resolution policy `1`), `Contract` clause kind `6` with a
  16-byte payload (parameter ordinal, result condition, failure disposition,
  reserved zero), `NativeParameter` Contract kind `10`, and Feature ID `2`,
  rule revision `1`, for Windows named native imports. Feature ID `1` remains
  assigned to `ScalarInitOutput`; neither assignment is a canonical registry
  change.
- The branch-local aggregate-pointer experiment uses provisional Feature ID
  `4` and Contract kind `12`, as described above. These assignments are not
  canonical and do not change the normative specification.
- The prototype represents `write ptr<T>` with the existing inline
  `TypeSpec` family encoding for `ptr<T>` (`familyCode=0x8C`, writable
  qualifier `0x01`, and the atomic code for `T`). This reuses the existing
  TypeSpec grammar and does not assign a new form.
- The prototype admits bodyless source-authored imports for named DLL
  exports under the Windows System32 policy, with directly Blittable scalar
  parameters and writable scalar pointer outputs. Output validity conditions
  and failure dispositions remain provisional. Mandatory verification still
  requires `Effects.RequiresUnsafeCaller`. Private semantic producer and
  native-package paths preserve and revalidate direct-import and writable-
  output facts and bind consumers against the produced dependency. This does
  not establish canonical TRRM assignments, complete JIT/module-set/graph
  dependency support, or native runtime behavior.
