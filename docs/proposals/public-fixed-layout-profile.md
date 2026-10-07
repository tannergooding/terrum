# First public fixed-layout profile

**Status: unapproved**

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

Should the first public fixed-layout profile opt in Windows x64 compact
`Copy` structs with natural-aligned sequential layout, while preserving
existing artifact contracts?

### Summary

The accepted representation model is defined, but there is no profile-specific
source schema or implementation contract for independently replaceable
public layouts. Choosing source syntax, eligible fields, target limits and
compatibility behavior is required before these layouts can cross a public
separate-compilation boundary.

### Recommendation

Adopt one explicit, opt-in Windows x64 profile as a provisional first slice:

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
- Admit public, non-unique compact `Copy` structs up to 16 bytes after final
  tail padding. Permit only `int32`, `uint32`, `int64`, `uint64`, `char16`, and
  nested structs that independently satisfy this same profile. Reject
  recursive value containment, `bool`, floating types, references, pointers,
  arrays, unique or managed values, and unions.
- Use natural field alignment only. Do not enable custom packing or
  aggregate over-alignment in this slice; `Transparent` remains unchanged.
- Use the existing Windows x64 target requirement (`1`). Identify the logical
  layout promise as `windows-x64-natural-sequential-v1`; this descriptive
  profile name is not a new numeric target or wire identifier.
- Persist and validate the explicit profile commitment, target requirement,
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

This narrow profile reuses the existing natural-layout rule without claiming
that it is a universal representation. The 16-byte bound is an initial
implementation limit, not a language-wide maximum.

## Proposed layout and compatibility contract

The selected Windows x64 storage entries for this profile are:

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
contributes its already-validated size and alignment. The 16-byte bound
applies only to final padded size, not to alignment. Padding remains
uninitialized and this commitment does not authorize raw byte copying,
validate arbitrary bit patterns, add atomicity, or define procedure-call
classification.

The logical structure metadata must retain enough information to compare the
same promise across producers and consumers: profile name, target requirement,
size, aggregate alignment, and each field's order, type, access/mutation
contract, and offset. Existing metadata already carries most field shape;
persisting the profile commitment and aggregate alignment is the missing
semantic distinction. The concrete encoding and any new feature/revision
values remain unassigned until the metadata review.

| Producer / consumer case | Recommended behavior |
|---|---|
| Existing artifact without this commitment | Load only under its existing profile and compatibility checks; do not infer or synthesize `Blittable`. |
| New consumer requiring this profile, but producer lacks it | Reject before execution. |
| Producer and consumer both declare this profile with identical target and field contract | Accept after validating the recorded layout. |
| Target, profile, size, alignment, field order/type/access, or offset differs | Reject as incompatible before execution. |
| Legacy consumer with a new producer | Preserve the existing contract where its format permits; otherwise reject the unsupported revision before execution. Never synthesize `Blittable`; the exact legacy-reader behavior depends on metadata review. |
| Existing source is recompiled before migration activation | Preserve current source acceptance; do not infer `Blittable` or activate new diagnostics solely by recompilation. |
| Source is compiled after an explicit migration activates the accepted rule | Diagnose public instance fields that lack a fixed-representation commitment; the activation mechanism must be specified before enforcement. |

This proposal does not change private Sequential behavior or bless existing
unannotated artifacts as public fixed-layout values.

## Scope and validation

Before source adoption, review the proposed schemas and source-migration
activation against the selected core-library identity and existing compiler
diagnostic/versioning mechanisms. Keep wire identifiers and native
procedure-call classifications unassigned until those contracts are reviewed.

Validate natural alignment, interior and tail padding, nested layouts, the
size bound, malformed or mismatched profile metadata, and compatible versus
incompatible module replacement. Compare independently compiled producer and
consumer storage layouts with an independent Windows x64 C fixture;
Terrum-to-Terrum round trips alone do not establish layout conformance.

This proposal does not add a public layout promise, packing or over-alignment
support, `Union`, a universal wire encoding, or a stable ABI. Existing private
Sequential layouts, transparent values, semantic-module contracts and native
test carriers retain their current boundaries.
