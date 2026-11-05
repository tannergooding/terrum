# ImmutableData Feature admission

> [!IMPORTANT]
> **Unapproved Feature-admission proposal.** This remains a separate
> registration/closure decision, not approval of the row schema, target
> assignments, values, witnesses, static publication, implementation or
> conformance.

## Candidate registration and owner

Recommend `19/1=ImmutableDataIdentities`, governing edition 1, with no
unconditional Feature prerequisites (`[]`). The complete target semantics,
DefId candidates, owner rules, binding and use-dependent closure are in
[ImmutableData declaration references](immutable-data-declaration-reference.md).
This proposal preserves only the separate decision to register/admit Feature
19 and its per-Module requirements.

Every using Module that retains an ImmutableData identity must require
Feature 19 for all retained contexts in the semantic owner, not only executed
reads. This is per-Module closure, not an artifact-wide Feature union; a
provider's manifest does not supply a consumer's missing Feature 19.
Feature `17/1` predicate graphs and `11/1` scalar frozen values remain
independent: neither implies Feature 19, and Feature 19 adds no edge to either.
It adds no DefId, section, clause, TypeSpec form, source syntax or runtime
record.

The current candidate uses direct DefIds `0x22=InterfaceConstDef` and
`0x23=WitnessAssociatedConst` for ImmutableData kind 1, and
`0x24=StaticField` for kind 2; only 22 and 24 extend Import/Export targets.
These assignments are separately unapproved in
[ImmutableData definition kinds](immutable-data-definition-kinds.md).
AssociatedConstDef declaration identity `0x25` remains owned by its distinct
proposal and is not a kind-1 target.

## Preserved boundaries and gates

Feature 19 does not supply missing declaration, value, associated-constant/
witness, StaticField/publication, backing or cleanup codecs. Feature 12,
static-field publication/load lifecycle and intrinsic registration remain
separate decisions, owned respectively by the
[static-field persistence](immutable-static-field-persistence.md) and
[intrinsic registration identity](intrinsic-registration-identity.md)
proposals where applicable.

The accepted registry remains unchanged until the Feature candidate and its
registry/manifest/schema changes are reviewed and adopted. Implementation
support belongs in the
[implementation reference](../implementation.md) and
[conformance handoff](../implementation/conformance-gaps.md); this proposal
asserts no Feature registration, runtime behavior or compiler support.
