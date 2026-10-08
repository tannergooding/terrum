# First public fixed-layout implementation slice

**Status: the bounded Windows x64 implementation direction, no separate
profile identity, transitive field eligibility, explicit `Blittable` metadata
requirement, and source-language-version migration boundary are accepted.
Implementation support is not established. Attribute schemas, metadata
encoding, and source-version value/tooling selector remain open. Core
primitives are `Blittable` by their type contract; natural alignment remains
target-specific. Windows x64 native ABI classification is required for
interop; by-value aggregates classified as indirect use a shadow copy, with
no proposal-specific 16-byte size limit.**

## Accepted direction

The accepted semantics and implementation boundary are recorded in the
[data-layout specification](../specs/data-layout-and-abi.md), the
[metadata specification](../specs/il-metadata.md), the
[roadmap](../roadmap.md), and the
[implementation reference](../implementation.md). No profile identity,
artifact encoding, or compiler support is created by this proposal document.

## Remaining schema decisions

The registered source schemas are not accepted yet. The recommendation is a
parameterless `[Blittable]` marker and `[Layout]` with one required layout-kind
enum argument, accepting only `Sequential` for this slice. Their resolved
identities, selected-core-library registration, primary-declaration placement,
multiplicity, and invalid/conflicting-argument diagnostics remain to be
specified. No library or metadata identity is assigned here.

Metadata encoding and source-version/tooling selection also remain open.
Preserve the accepted requirement that a consumer validate explicit
`Blittable` metadata against the existing layout and field facts before
execution; see the [metadata specification](../specs/il-metadata.md).
Historical compatibility for beta artifacts is not required; the metadata
specification may evolve its reader and writer together.

## Scope and validation

Before implementation, define the proposed schemas and select the exact
source-language version and tooling selector for the migration against the
selected core-library identity and existing compiler diagnostic/versioning
mechanisms. Keep wire identifiers unassigned. The storage fixture below does
not establish native call-lowering conformance.

Validate natural alignment, interior and tail padding, nested and larger
layouts, malformed or mismatched layout metadata, and compatible versus
incompatible module replacement. Compare independently compiled producer and
consumer storage layouts with an independent Windows x64 C fixture;
Terrum-to-Terrum round trips alone do not establish layout conformance.

The accepted direction does not establish implemented public-layout support,
packing or over-alignment support, `Union`, a universal wire encoding, or a
stable ABI. Existing private Sequential layouts, transparent values,
semantic-module contracts and native test carriers retain their current
boundaries.

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
contracts. These tests do not verify native call lowering.

A separate Windows x64 C fixture verifies the selected scalar storage and the
same nested, padded and larger shapes using `sizeof`, `__alignof` and
`offsetof`. This is evidence for the proposed storage calculation only; it
does not activate a public layout commitment, approve the remaining proposal,
test procedure-call classification or raw-copy behavior.
