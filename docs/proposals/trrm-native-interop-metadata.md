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
| `NativeComMethod` | `MethodDef` | Explicit vtable slot. The method's existing signature describes the native call; the owning `TypeDef` identifies the interface. |
| `NativeComOwner` | `TypeDef` | The COM interface represented by this unique owner type. Reuse its existing layout and `Cleanup` contract for storage and release behavior. |
| `NativeComAcquisition` | `MethodDef` | Output parameter ordinal, resulting owner `TypeDef`, success rule, and whether failure may still write an owned output. |

Payload widths and enum assignments are intentionally unassigned in this
proposal. The reader must validate all references, unique slots, interface
inheritance, signature arity, output ordinals, owner/interface compatibility,
and failure cleanup requirements before accepting the native contract.
Unknown required clauses must reject; readers must not silently ignore
unsupported interop semantics.

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
