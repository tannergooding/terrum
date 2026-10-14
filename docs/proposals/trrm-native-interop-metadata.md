# Native imports and COM metadata in TRRM

**Status: UNAPPROVED proposal for review.** This proposal adds no records to
the canonical TRRM design and changes no current implementation. It proposes
the smallest generic metadata surface needed to describe native DLL imports
and COM calls in a Terrum dependency, so the compiler can consume the
dependency's declarations instead of owning an API catalog.

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

The current native catalog fills that gap out of band. For DX11 it also holds
COM IIDs and inheritance, vtable slots, native output ownership, cleanup,
effects, and argument adaptations. A source `[NativeImport(key)]` selects a
catalog entry rather than producing the binding metadata that a binary
dependency can carry. This proposal moves only the irreducible declarations
into TRRM; it does not preserve catalog-only convenience behavior.

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

The exact table kind, field widths, string encoding requirements, and policy
values remain for review. A first implementation can limit the row to named
exports and the selected Windows system-library policy.

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
| `NativeComAcquisition` | `MethodDef` | Output parameter ordinal, resulting owner `TypeDef`, and whether failure may still write an owned output. It composes with `NativeOutput`'s result condition. |

Payload widths and enum assignments are intentionally unassigned in this
proposal. The reader must validate all references, unique slots, interface
inheritance, signature arity, unique parameter ordinals, output ordinals,
owner/interface compatibility, and failure cleanup requirements before
accepting the native contract. A `NativeOutput` must refer to a writable
parameter and a result condition expressible for that method's result type.
A `NativeComAcquisition` must refer to that output and to an owner for the
declared interface.
Unknown required clauses must reject; readers must not silently ignore
unsupported interop semantics.

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

The source annotations below illustrate the proposed information flow; their
exact spelling is still open. Each library method has its ordinary Terrum
signature and export contract. Native and COM records add only facts that
cannot be recovered from that signature.

### `QueryPerformanceCounter`

The Windows API is a `BOOL` function with one output pointer to a signed
64-bit `LARGE_INTEGER`. The Terrum declaration keeps the native return width
as `int32`; the `out` parameter gives the pointer's pointee type.

**Interop library declaration**

```terrum
namespace Terrum.Interop.Windows;

[NativeImport("Kernel32.dll", "QueryPerformanceCounter", System32)]
[NoThrow]
[NativeParameter(0, Access = Write, NoEscape = true)]
[NativeOutput(0, Success = NonZero, Failure = Unspecified)]
public extern int32 QueryPerformanceCounter(out int64 performanceCount);
```

**Consumer use**

```terrum
int32 Main() {
    int32 succeeded = QueryPerformanceCounter(out var ticks);
    if (succeeded == 0) return 1;
    ObserveCounter(ticks);
    return 0;
}

[NoThrow]
void ObserveCounter(int64 ticks) {}
```

The exported declaration produces or reuses these TRRM records:

| Record | Example value |
|---|---|
| `MethodDef` / `MethodSig` / `Param` | `int32 QueryPerformanceCounter(out int64)`; parameter 0 is an output destination. |
| `NativeImport` | Method `QueryPerformanceCounter` → `Kernel32.dll!QueryPerformanceCounter`, resolved by the selected Windows system-library policy. |
| `Effects` | `NoThrow`. |
| `NativeParameter` | Parameter 0 is writable and non-retained; its pointee is one `int64`. |
| `NativeOutput` | Parameter 0 is valid when the `int32` result is nonzero. On zero, its state is unspecified and the caller must not read it. |

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

```terrum
namespace Terrum.Interop.Dxgi;

[NativeImport("Dxgi.dll", "DXGIGetDebugInterface1", System32)]
[NoThrow]
[NativeParameter(1, Access = Read, NoEscape = true)]
[NativeParameter(2, Access = Write, NoEscape = true)]
[NativeOutput(2, Success = HResultSucceeded, Failure = MayBeWritten)]
[NativeComAcquisition(2, Owner = DxgiDebugOwner)]
public extern int32 DXGIGetDebugInterface1(
    uint32 flags,
    ptr<Guid> riid,
    out ptr<IDXGIDebug> ppDebug);

[NativeComInterface(
    Iid = "119E7452-DE9E-40FE-8806-88F90C12B441",
    Base = IUnknown)]
public interface IDXGIDebug : IUnknown {
    [NativeComMethod(Slot = 3)]
    int32 ReportLiveObjects(Guid apiId, int32 flags);
}

[NativeComOwner(Interface = IDXGIDebug)]
public unique struct DxgiDebugOwner {
    ptr<IDXGIDebug> Pointer;

    [NoThrow] [Cleanup] public void Close() {
        Pointer.Release();
    }
}
```

**Consumer call**

```terrum
int32 ReportAllLiveObjects(DxgiDebugOwner debug) {
    return debug.Pointer.ReportLiveObjects(DXGI_DEBUG_ALL, DXGI_DEBUG_RLO_ALL);
}
```

`IUnknown` is supplied by the shared COM declarations: `QueryInterface`,
`AddRef`, and `Release` occupy slots 0, 1, and 2. The interop library also
declares `Guid` with the Windows layout (16 bytes, 4-byte alignment), and
defines the `DXGI_DEBUG_ALL` GUID and `DXGI_DEBUG_RLO_ALL` value (`0x7`).
An ordinary Terrum wrapper can transfer the raw output pointer into
`DxgiDebugOwner`; the compiler does not synthesize that wrapper.

| Record | Example value |
|---|---|
| `NativeImport` | `DXGIGetDebugInterface1` → `Dxgi.dll!DXGIGetDebugInterface1`, Windows system-library policy. |
| `MethodSig` / `Param` | `int32(uint32, ptr<Guid>, out ptr<IDXGIDebug>)`; parameter 1 is `REFIID`, parameter 2 is `void**`. |
| `NativeParameter` | Parameter 1 is read-only and non-retained; parameter 2 is writable and non-retained. |
| `NativeOutput` | Parameter 2 is valid when the HRESULT is nonnegative. Conservatively, treat it as possibly written on failure. |
| `NativeComAcquisition` | Parameter 2 produces an owned `IDXGIDebug`, represented by `DxgiDebugOwner`; release any non-null failure output. |
| `NativeComInterface` | `IDXGIDebug` has IID `119E7452-DE9E-40FE-8806-88F90C12B441` and base `IUnknown`. |
| `NativeComMethod` | `ReportLiveObjects` is slot 3 with signature `int32(Guid, int32)`; `IUnknown.Release` is slot 2 and releases a reference. The `Guid` layout and target requirement let the backend derive aggregate passing. |
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
- It does not change accepted language, ABI, ownership, or lifetime
  semantics.
- Source syntax for authoring the native binding and COM clauses remains
  open; it must produce the same dependency metadata without a compiler-owned
  symbol catalog.
- The current DX11 path also relies on fixed-array/storage-record constraints
  and mapped-buffer behavior. This proposal does not encode those as import
  metadata; review whether ordinary source wrappers and existing target
  layout contracts suffice, or whether a separate generic storage contract is
  required.
- Review whether the first slice needs explicit metadata for reference-count
  cloning beyond owned outputs and ordinary `Cleanup`, or whether that
  behavior should remain a Terrum wrapper.
- Exact table/clause assignments and the required-feature identifier must be
  allocated only after this proposal is reviewed.
