# First public fixed-layout implementation slice

**Status: the bounded Windows x64 implementation direction, no separate
profile identity, transitive field eligibility, explicit `Blittable` metadata
requirement, and source-language-version migration boundary are accepted.
Public source-level layout support is not established. The provisional
bound-IR/storage slice below is implemented under an **UNAPPROVED**
recommendation. The registered source schemas are specified by the Windows
target contract; their compiler adoption, metadata encoding and source-version
value/tooling selector remain pending. Core primitives are `Blittable` by their
type contract; natural alignment remains target-specific. Windows x64 native
ABI classification is required for interop; by-value aggregates classified
as indirect use a shadow copy, with no proposal-specific 16-byte size limit.**

## Accepted direction

The accepted semantics and implementation boundary are recorded in the
[data-layout specification](../specs/data-layout-and-abi.md), the
[metadata specification](../specs/il-metadata.md), the
[roadmap](../roadmap.md), and the
[implementation reference](../implementation.md). No profile identity,
artifact encoding, or compiler support is created by this proposal document.

## Source schema and remaining physical decisions

The [registered source schemas](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas)
select parameterless `[Blittable]` and `[Layout]` with required LayoutKind and
optional named uint32 Packing/Alignment. They define exact Core registration,
primary-only/nonrepeatable placement and invalid/conflicting-argument
diagnostics. This implementation slice still accepts only its bounded
Sequential scope; the wider logical target rules do not establish its support.
No library binary or metadata identity is assigned by this proposal.

Metadata encoding and source-version/tooling selection also remain open.
Preserve the accepted requirement that a consumer validate explicit
`Blittable` metadata against the existing layout and field facts before
execution; see the [metadata specification](../specs/il-metadata.md).
Historical compatibility for beta artifacts is not required; the metadata
specification may evolve its reader and writer together.

## Scope and validation

Before source migration, use the specified schemas and select the exact
source-language version and tooling selector for the migration against the
selected core-library identity and existing compiler diagnostic/versioning
mechanisms. Keep wire identifiers unassigned. The storage fixture below does
not establish native call-lowering conformance.

Validate natural alignment, interior and tail padding, nested and larger
layouts, malformed or mismatched layout metadata, and compatible versus
incompatible module replacement. Compare independently compiled producer and
consumer storage layouts with an independent Windows x64 C fixture;
Terrum-to-Terrum round trips alone do not establish layout conformance.

The accepted direction does not establish implemented public source-layout
support, packing or over-alignment support, `Union`, a universal wire encoding,
or a stable ABI. Existing private Sequential layouts, transparent values,
semantic-module contracts and native test carriers retain their current
boundaries.

## Test-only candidate evidence

The separate test-only candidate evaluator still evaluates explicitly marked
declarations without changing source attributes, binder or verifier behavior,
semantic-module metadata, or the native emitter. The production
bound-storage tests below exercise the provisional validator separately. The
candidate evaluator independently recomputes natural field offsets, aggregate
alignment and padded size, checks transitive candidate membership, and
compares declarations compiled with different structure indexes by nominal
name. The cases cover compatible body-only replacement, every storable core
scalar kind, nested storage, padding, larger aggregates, non-`Copy` and
ineligible nested fields, forged offsets/widths/sizes/alignment claims, cycles
and excessive nesting, plus incompatible commitment, target, nominal, field,
access, mutation and unsafe contracts. These tests do not verify native call
lowering.

A separate Windows x64 C fixture verifies every storable core scalar, mixed
floating and nested storage, padding, and aggregates larger than 16 bytes using
`sizeof`, `__alignof` and
`offsetof`. This is evidence for the proposed storage calculation only; it
does not activate a public layout commitment, approve the remaining proposal,
test procedure-call classification or raw-copy behavior.

## Provisional bound-storage implementation

**UNAPPROVED recommendation: use an explicit
`BoundStruct.BlittableTargetRequirement` (`0` means uncommitted, `1` means the
existing Windows x64 target requirement), together with `IsSequential`, for
the first production storage-validation seam.** This is a bound-IR opt-in,
not a registered source attribute, a source-language-version selector or a
new layout-profile identity. It lets metadata consumers enforce the accepted
storage contract without activating the selected source schemas or assigning
their physical metadata.

**UNAPPROVED encoding recommendation: bounded semantic-module revision 78
adds an `int32` target requirement and a checked Boolean Sequential marker
after each structure's existing Transparent Boolean.** An explicit version
prevents old readers from silently discarding a commitment. Existing revisions
76 and 77 retain their byte shapes and legacy behavior. Revision 78 requires
at least one commitment and recomputes transitive storage facts before exposing
references or execution. Missing nested commitments, unknown targets,
ineligible fields, cycles, and forged sizes, offsets or widths reject.
Replacement compares commitments and layout kinds as well as the existing
nominal, field, access, mutability and caller-unsafe contracts. Private
Sequential remains layout-coupled rather than a replacement commitment.
Canonical TRRM `(1, 0)` is not changed, and no reserved bits are repurposed.
Combining this extension with revision-77 private span IR is explicitly
unsupported pending a combined schema.

The production storage validator has no 16-byte eligibility limit and uses
natural Windows x64 alignment, including tail padding. The existing executable
verifier and module execution paths remain limited to compact aggregates of
at most 16 bytes; larger committed layouts can be validated as storage, not
executed by this slice. This is an implementation limitation, not an ABI
classification rule. The current storable Core primitives are eligible by
their existing contracts. Managed references, owners, borrowed views and
compiler-sealed native carriers do not acquire a public storage contract.

Imported committed types use existing public-field binding and verification:
readonly, receiver mutation, initialization, private access and unsafe-caller
requirements are not relaxed. No source migration is activated: legacy Auto
and Transparent declarations keep their existing behavior, and `[Blittable]`
is not recognized by the compiler's source registration. Source-schema
adoption, canonical metadata encoding and source-version/tooling selection
remain pending. This
provisional support does not change the accepted or unresolved statuses above.
