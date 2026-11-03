# ImmutableData Feature admission

> [!IMPORTANT]
> **Unapproved Feature-admission proposal.** This remains a separate
> registration/closure decision, not approval of the row schema, target
> assignments, values, witnesses, static publication, implementation or
> conformance.

## Decision and boundary

Recommend candidate **`19/1=ImmutableDataIdentities`**, governing edition 1,
with no unconditional Feature prerequisites (`[]`). It owns per-Module
admission of retained ImmutableData identity rows and their declaration/
owner resolution, comparison, binding and remapping.

The complete target semantics, DefId candidate mapping, owner rules,
per-Module detection and use-dependent closure are integrated in
[ImmutableData declaration references](immutable-data-declaration-reference.md).
That document is the semantic owner. This proposal preserves the separate
approval question of whether Feature 19 is registered with the assigned
manifest/registry and requirements mechanism.

## Exact DefId targets

The current candidate composition uses direct `0x22=InterfaceConstDef` and
`0x23=WitnessAssociatedConst` targets for ImmutableData kind 1, and
`0x24=StaticField` for kind 2. Import/Export additions are 22/24 only.
These assignments are independently unapproved in
[ImmutableData definition kinds](immutable-data-definition-kinds.md); they
are repeated here to make the proposed Feature scope unambiguous. Feature
19 adds no DefId, section, clause, TypeSpec form, source syntax or runtime
record.

## Per-Module detection and closure

The proposed registration is exactly `19/1`, with `governingEdition=1` and
`prerequisites=[]`. It remains independent of Feature `17/1`
general-predicate graphs and Feature `11/1` scalar frozen values. A retained
ImmutableData identity requires Feature 19 in each using Module; a graph or
value use adds only its own independently required closure. Neither Feature
17 nor a provider's manifest supplies a consumer's missing Feature 19.

Detection and conditional closure cover all retained contexts described by
the [semantic owner](immutable-data-declaration-reference.md#feature-admission-and-per-module-closure),
not only executed reads. Feature 19 does not admit missing declaration,
value, associated-constant/witness, StaticField/publication, backing or
cleanup codecs. Feature 12, static lifecycle and intrinsic registration
remain separate. There is no artifact-wide Feature union or fallback.

The accepted registry currently assigns other Feature meanings and
prerequisites; it remains unchanged until this candidate and its corresponding
registry/manifest/schema changes are reviewed and adopted. The separate
scalar-only 11/1 candidate and predicate-only 17/1 candidate are not widened
by this proposal.

## Owner integration and remaining gates

Adoption requires coordinated changes to the ImmutableData logical owner,
Feature registry/manifest projections, per-Module detection, consumer
before-use checks, binding/replacement and rewriters. The exact candidate
cases and cross-feature closure table are owned by
[ImmutableData declaration references](immutable-data-declaration-reference.md#feature-admission-and-per-module-closure).
Static publication remains with
[immutable static-field persistence](immutable-static-field-persistence.md);
intrinsic identity/catalog admission remains with
[intrinsic registration identity](intrinsic-registration-identity.md).

Current implementation support belongs in the
[implementation reference](../implementation.md) and
[conformance handoff](../implementation/conformance-gaps.md). This document
asserts no Feature registration, runtime behavior or compiler support.
