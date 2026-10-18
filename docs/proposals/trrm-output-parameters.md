# Executable output and place integration

**Status: core executable outputs are an accepted TRRM 1.0 GA requirement;
their complete physical contract remains unapproved.** The
[joint output/failure/ownership proposal](trrm-output-failure-ownership.md)
owns composition and its acceptance gate. This document retains output/place
encoding decisions, not a second GA requirement or a list of completed
ScalarInitOutput increments.

## Recommended integration

Use the accepted [reference and initialization modes](../specs/language/references.md)
and [publication relationships](../specs/language-contracts.md#22-declared-outcome-relationships).
Persist actual places, complete types, state and versions, formal-to-argument
mapping, conditional publication, and exceptional-state/cleanup dependencies.
Do not infer success from a Boolean return or a function name where the
authored contract says otherwise.

The current [bounded implementation](../implementation/output-profiles.md#provisional-trrm-output-bodies)
is useful evidence, not the proposed final wire profile. Its Feature 1,
minimum-revision field, old Param modes/opcodes, three-block limit and unsafe
tail marker are not constraints on the next draft.

| Area | Required integration decision |
|---|---|
| Declarations | Use the selected Param/reference type and OutputState applicability; define any richer discriminator codec under the joint gate |
| Places | Preserve storage identity, activation, vacancy, initialized-null, access, owner/root/backing closure and generation |
| Calls | Stage arguments once in source order; preserve formal mapping, reservations, exactly-once transfer and call-specific destination/status relationships |
| Publication | Publish only complete outputs under their declared normal outcome; clean unpublished values on failure without transactional rollback |
| Body forms | Admit only forms that carry all required state, CFG, region and cleanup facts; do not impose today's compact/profile exclusions on future encodings |
| Admission | Register exact capabilities and prerequisite closure, not minimum-number roll-forward or a runtime-policy flag |

### Minimal slice or complete state model

A scalar-only, fail-fast slice can be an implementation milestone, but cannot
be the sole source of the place or outcome schema. Recommend defining the
shared place/publication/exception model first, then admitting bounded
projections. Extending that model may replace draft records and existing
IDs; no legacy scalar-profile decoder is required.

Targets are the [instruction set](../specs/il-instruction-set.md#executable-subset-versus-assigned-decode-shapes),
metadata Param/OutputState records, the unified registry, logical records,
and their conformance chapters. Exact output/handler/owner codecs remain
blocking design decisions, not evidence that outputs should stay absent.

## Entry-state place-use ordinals

**Concrete recommendation, unapproved:** count each place in `startPlaces`
or `inheritedPlaces` as a use at block entry with ordinal `0`.
`placeLastUses` records the maximum of that use and all instruction,
terminator and end-place uses. Coincident ordinals are allowed, including in
a zero-operation block. Inherited state continues an activation; it does
not restart the place or clear its initialization.

This closes the representability hole for an entry-activated place with no
later operand use. Integrate it with the instruction chapter's place-use
inventory, registry shapes and conformance cases; do not infer its approval
from the provisional writer/reader implementing it.

## Required cases and remaining gates

Cover all admitted referent/result types, imported and forwarded outputs,
field/indexed destinations, named arguments, aliases, exact place generations,
copied/reassigned status values, initialized-null, joins, loops and exceptions.
Include a formed owner failing its postcondition, moved inputs, cleanup
ordering, malformed entry/last-use summaries and incompatible replacement.

Unsupported combinations must decline affected use rather than publish a
default output or erase a dependency. Trusted selective inspection and
whole-artifact validation remain distinct; recognizing a place shape does
not establish executable support. Required compiler adjustments are tracked
in the [conformance inventory](../implementation/conformance-gaps.md).
