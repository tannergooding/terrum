# First public fixed-layout implementation slice

**Status: no separate profile identity (accepted); the proposed support
slice, field eligibility, compatibility encoding and source migration remain
unapproved. Windows x64 native ABI classification is required for interop;
by-value aggregates classified as indirect use a shadow copy, with no
proposal-specific 16-byte size limit.**

## Current boundary

The accepted data-layout contract defines `Blittable` as a per-target-profile
commitment to size, alignment and field offsets, with transitive field
eligibility. `Sequential` controls declaration order but does not freeze a
layout; packing and aggregate alignment are separate controls. The source
grammar leaves their registered schemas and target limits to the profile.

The current private `[Layout(Sequential)]` subset uses natural alignment and
does not establish a public ABI. Existing semantic-module layout contracts
and native test carriers are independently scoped and do not imply a
`Blittable` commitment.

## Question

Should an initial support slice implement the existing target-specific
`Blittable` plus `Sequential` contract for public Windows x64 compact `Copy`
structs, while preserving existing artifact contracts?

### Summary

The accepted representation model is defined, but there is no target-specific
source schema or implementation contract for independently replaceable
public layouts. Choosing eligible fields, target limits and compatibility
behavior is required before these layouts can cross a public
separate-compilation boundary.

### Recommendation

Treat the following as a provisional implementation slice for the existing
Windows x64 `Blittable` plus `Sequential` contract, not as a new layout
profile or semantic commitment:

- Require both an explicit `Blittable` commitment and `Sequential` layout;
  resolve them through registered semantic-attribute identities and schemas,
  not by attribute name alone.
- Define `Blittable` as a parameterless marker on a struct declaration and
  `Layout` as one required layout-kind enum argument, accepting only
  `Sequential` in this profile. Both are primary-declaration-only,
  non-repeatable attributes; conflicting layout kinds and unknown arguments
  are errors. Their identities must be the resolved declarations from the
  selected core library, not unqualified token spellings. Attribute identity
  registration remains a prerequisite; this proposal assigns no new library
  or metadata identity.
- Admit public, non-unique compact `Copy` structs without a proposal-specific
  size limit.
  Permit only `int32`, `uint32`, `int64`, `uint64`, `char16`, and
  nested structs that independently satisfy this same implementation slice.
  Reject recursive value containment, `bool`, floating types, references,
  pointers, arrays, unique or managed values, and unions.
- Use natural field alignment only. Do not enable custom packing or
  aggregate over-alignment in this slice; `Transparent` remains unchanged.
- Use the existing Windows x64 target requirement (`1`). `Sequential` follows
  that target's layout requirements, and `Blittable` commits the representation
  for that target. Do not introduce a separate profile name or identity.
- Match the Windows x64 system ABI's procedure-call classification. A by-value
  aggregate classified as indirect is passed through a caller-created shadow
  copy, not the caller's actual storage and not a source-level
  `implicit ref<T>`.
- Persist and validate the explicit `Blittable` commitment, target requirement,
  final size, aggregate alignment, and ordered field type/access/offset
  records wherever an independently replaceable consumer relies on the
  representation. Recompute and check those values rather than trusting
  producer-supplied offsets. Reject unsupported targets and incompatible
  producer replacements before execution.
- Keep existing unannotated module artifacts under their existing
  compatibility rules; never infer that they made this `Blittable`
  commitment. New source declarations that expose public instance fields
  follow the accepted fixed-representation rule after an explicit,
  reviewed source-compatibility migration is activated. Recompiling existing
  source alone must not activate new diagnostics, and retained artifacts must
  not be retroactively rejected.

This narrow implementation slice reuses the existing natural-layout rule
without claiming that it is a universal representation. It has no
proposal-specific struct-size bound.

## Proposed layout and compatibility contract

The selected Windows x64 storage entries for this implementation slice are:

| Field type | Size | Natural alignment | Representation |
|---|---:|---:|---|
| `int32` | 4 | 4 | Little-endian two's-complement integer |
| `uint32` | 4 | 4 | Little-endian unsigned integer |
| `int64` | 8 | 8 | Little-endian two's-complement integer |
| `uint64` | 8 | 8 | Little-endian unsigned integer |
| `char16` | 2 | 2 | Little-endian 16-bit code unit |

Align each field start to that field's target natural alignment and place
fields in declaration order. The aggregate alignment is the maximum field
alignment; round the final size up to that alignment. A nested field
contributes its already-validated size and alignment. There is no
proposal-specific maximum final size. Padding remains uninitialized and this
commitment does not authorize raw byte copying, validate arbitrary bit
patterns, or add atomicity. Procedure-call classification follows the target
system ABI as specified above.

The logical structure metadata must retain enough information to compare the
same promise across producers and consumers: explicit `Blittable` and
`Sequential` commitments, target requirement, size, aggregate alignment, and
each field's order, type, access/mutation contract, and offset. Existing
metadata already carries most field shape; persisting the `Blittable`
commitment and aggregate alignment is the missing semantic distinction. The
concrete encoding and any new feature/revision values remain unassigned until
the metadata review.

| Producer / consumer case | Recommended behavior |
|---|---|
| Existing artifact without this commitment | Load only under its existing profile and compatibility checks; do not infer or synthesize `Blittable`. |
| New consumer requiring this target-specific commitment, but producer lacks it | Reject before execution. |
| Producer and consumer both declare the same target-specific `Blittable` and `Sequential` commitments with identical field contracts | Accept after validating the recorded layout. |
| Target, commitment, size, alignment, field order/type/access, or offset differs | Reject as incompatible before execution. |
| Legacy consumer with a new producer | Preserve the existing contract where its format permits; otherwise reject the unsupported revision before execution. Never synthesize `Blittable`; the exact legacy-reader behavior depends on metadata review. |
| Existing source is recompiled before migration activation | Preserve current source acceptance; do not infer `Blittable` or activate new diagnostics solely by recompilation. |
| Source is compiled after an explicit migration activates the accepted rule | Diagnose public instance fields that lack a fixed-representation commitment; the activation mechanism must be specified before enforcement. |

This proposal does not change private Sequential behavior or bless existing
unannotated artifacts as public fixed-layout values.

## Scope and validation

Before source adoption, review the proposed schemas and source-migration
activation against the selected core-library identity and existing compiler
diagnostic/versioning mechanisms. Keep wire identifiers unassigned. Windows
x64 procedure-call classification follows the system ABI; the storage-layout
fixture below does not establish its call-lowering conformance.

Validate natural alignment, interior and tail padding, nested and larger
layouts, malformed or mismatched layout metadata, and compatible versus
incompatible module replacement. Compare independently compiled producer and
consumer storage layouts with an independent Windows x64 C fixture;
Terrum-to-Terrum round trips alone do not establish layout conformance.

This proposal does not add a public layout promise, packing or over-alignment
support, `Union`, a universal wire encoding, or a stable ABI. Existing private
Sequential layouts, transparent values, semantic-module contracts and native
test carriers retain their current boundaries.

## Test-only candidate evidence

The test suite now evaluates explicitly marked candidate declarations without
changing source attributes, binder or verifier behavior, semantic-module
metadata, or the native emitter. It independently recomputes natural field
offsets, aggregate alignment and padded size, checks transitive candidate
membership, and compares declarations compiled with different structure
indexes by nominal name. The cases cover compatible body-only replacement,
primitive and nested storage, padding, unsupported types, forged
offsets/widths/sizes/alignment claims, cycles and excessive nesting, plus
incompatible commitment, target, nominal, field, access, mutation and unsafe
contracts. The test-only prototype's 16-byte admission cutoff is not a
contract; native call tests must verify shadow-copy passing for aggregates
classified as indirect by Windows x64.

A separate Windows x64 C fixture verifies the selected scalar storage and the
same nested, padded and larger shapes using `sizeof`, `__alignof` and
`offsetof`. This is evidence for the proposed storage calculation only; it
does not activate a public layout commitment, approve the remaining proposal,
test procedure-call classification or raw-copy behavior.
