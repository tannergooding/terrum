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
unannotated declarations and existing artifact contracts?

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
- Admit public, non-unique compact `Copy` structs up to the existing 16-byte
  layout bound. Permit transitively opted-in nested structs and the currently
  supported fixed-width integer and `char16` fields; exclude `bool`, floating
  types, references, pointers, unique or managed values, and unions from this
  first profile.
- Use natural field alignment only. Do not enable custom packing or
  aggregate over-alignment in this slice; `Transparent` remains unchanged.
- Persist the selected profile identity, size, aggregate alignment and ordered
  field offsets wherever an independently replaceable consumer relies on the
  representation. Reject unsupported targets and incompatible producer
  replacements before execution.
- Preserve unannotated source and retained module contracts. Do not
  retroactively require an annotation, reinterpret existing compact layouts,
  or treat current native fixtures as published ABI commitments.

This narrow profile reuses the existing natural-layout rule without claiming
that it is a universal representation. The 16-byte bound is an initial
implementation limit, not a language-wide maximum.

## Scope and validation

Before source adoption, specify the concrete attribute schemas, target
requirement identity, field eligibility, metadata shape and compatibility
migration. Keep wire identifiers and native procedure-call classifications
unassigned until those schemas are reviewed.

Validate natural alignment, interior and tail padding, nested layouts, the
size bound, malformed or mismatched profile metadata, and compatible versus
incompatible module replacement. Compare independently compiled producer and
consumer layouts with an independent Windows x64 C fixture; Terrum-to-Terrum
round trips alone do not establish ABI conformance.

This proposal does not add a public layout promise, packing or over-alignment
support, `Union`, a universal wire encoding, or a stable ABI. Existing private
Sequential layouts, transparent values, semantic-module contracts and native
test carriers retain their current boundaries.
