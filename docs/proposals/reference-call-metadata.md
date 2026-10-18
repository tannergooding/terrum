# Reference-call metadata

**Status: accepted source contracts; unapproved physical dependency codec.**
The [reference model](../specs/language/references.md),
[dependency relationships](../specs/language/dependencies-and-views.md),
[argument mapping](../specs/language/calls-and-inference.md), and
[output publication](../specs/language-contracts.md#22-declared-outcome-relationships)
own the selected semantics. The indexed-Body lexical unsafe-region contract is
already in the [instruction set](../specs/il-instruction-set.md#body-record-direct-block-index-and-regions).
This proposal concerns their remaining interchange integration, not another
source policy or an implementation-revision worklist.

## Problem and integration boundary

The current Dependency clause has reserved relationship fields rather than a
complete executable dependency contract. A borrowed result, forwarded backing,
capture into a surviving holder, and checked output publication cannot be
reconstructed from matching machine signatures or a serialized authorization
bit. General source approval does not fill those physical gaps.

The earlier decision deferred a physical dependency profile until a concrete
persisted provider/consumer path needed it. The
[native boundary proposal](trrm-native-interop-metadata.md#whole-source-relationships)
now supplies such a path. Review its proposed shared Dependency variants together
with this proposal rather than creating competing meanings for the same
source relationships. Bounded returned-token views remain a separate
[deferred application](token-span-result-forwarding.md).

Current hosted and private support belongs in the
[view/owner inventory](../implementation/view-and-owner-profiles.md),
[output inventory](../implementation/output-profiles.md), and
[implementation summary](../implementation.md). Those limits do not narrow
the accepted language.

## 1. Which dependency relations belong in the first callable profile?

**Recommendation, unapproved:** use one shared relationship vocabulary for
ordinary and native declarations. A bounded first profile can admit
synchronous, non-generic callables with one whole borrowed result and an
explicit closed set of formal sources. That is an implementation boundary,
not a prohibition on the accepted receiver, capture, or async contracts.

| Candidate tag | Meaning and obligations |
|---|---|
| Source kind `0x01` | Whole formal, using its zero-based declaration ordinal after binding, never an argument-evaluation position, local slot, table row, or hidden ABI parameter |
| Result ordinal `0` | The one whole result in this bounded profile |
| Relationship `0x01` | Borrow eligible source storage/substorage, retaining its authority and complete backing/root/lease obligations |
| Relationship `0x02` | Forward backing already carried by the source, without borrowing its descriptor place, acquiring a lease, or transferring ownership |
| Relationship `0x03` | Permit either relationship for the accepted single eligible source default; callers retain the union of applicable obligations, not a requirement that the producer satisfy both |

These are candidates, not current registry assignments. A by-value parameter
copy is not surviving caller storage. A direct storage selector must preserve
inherited backing obligations. Every listed source constrains the caller,
even when the implementation selects only one branch. Resolve eligible
sources before applying defaults; scalars, vacant outputs, or an excluded
receiver do not become sources merely to make a singleton.

Multiple sources require an explicit permitted set. A receiver needs its own
selector, not formal zero. Reject invalid/duplicate ordinals, unsupported
combinations, unknown tags and flags. Preserve complete types, access,
relations and sources through forwarding, copies, joins, reborrows, type
remapping and directed replacement.

### Minimal extension or shared codec

Filling the current Dependency payload's reserved tags is a small amendment
for the bounded result-only case. It is insufficient for retained holders,
native-output depth, publication/capture phases, or richer destinations.

Recommend consolidating the shared logical vocabulary and destination/phase
requirements with the native proposal before registration, even if that
requires replacing the draft payload. Do not permanently standardize a
native-only copy merely to preserve its provisional record. The exact shared
physical shape, capability closure, receiver/capture coverage and any
replacement of the current Dependency row remain review decisions.

The integration targets are the Dependency clause and its applicability in
[metadata](../specs/il-metadata.md#semantic-contracts-and-stable-binding),
the [registry](../specs/representation/registry.md), required
[logical records](../specs/representation/logical-records.md), and
[binding](../specs/representation/binding-and-replacement.md).
Changing unpublished payloads does not require retaining an old reader.
Exact-schema selection and consumer validation remain mandatory.

## 2. How should caller-unsafe declarations cross an import boundary?

This policy is settled, not an open approval question. Assigned Effects bit
`0x04` carries caller-unsafety. Only an explicit caller scope authorizes its
operations; a callee's body or internal unsafe block cannot erase the
published obligation. Readers reconstruct authorization from retained scope
structure rather than trusting an access-site bit.

## 3. How should executable Bodies validate caller-unsafe scope?

The accepted indexed-Body contract uses lexical region kind `0x06`.
Declaration Effects are not call authorization. Extend any additional Body
form with a reviewed structural scope contract rather than treating a
provisional tail marker as the general rule. The
[native Body proposal](trrm-native-interop-metadata.md#executable-body-carriers)
must integrate with this owner.

## 4. Which implementation and metadata gates remain for output calls?

The accepted reference, flow and argument-mapping chapters already define
eligible places, exact complete types, source-order staging, reservations,
and normal/exceptional destination states. Legacy `out` is not another
accepted reference parameter spelling.

Migrate binding, checked IR, traversal/rewriting, mandatory verification,
codecs, dependency binding and lowering together. Preserve destination and
value versions, alias reservations, ownership, cleanup, roots and actual
backing. Initialization mode `0x01` on an outer `ref<T>` TypeSpec must not be
confused with the implementation's old mode numbering.

The [joint output/failure proposal](trrm-output-failure-ownership.md) owns the
remaining composition gate; no inspectable OutputState row activates it.

## Acceptance cases and remaining decisions

Require independent source-free consumers, reordered/remapped formals and
types, implicit-reference materialization versus storage borrowing, direct
and inherited backing, multiple sources, copies/joins, receiver selectors,
capture/failure phases and incompatible replacement. Negative cases include
missing relations, fabricated fresh backing, expired roots/pins/holders,
stripped unsafe regions, invalid ordinal namespaces and output/status reuse.

Before registration, select the shared physical codec, exact manifest
requirements and complete admitted scopes. Before claiming support, verify
every affected producer/consumer route; the current verifier's
forwarded-backing facts cannot be reinterpreted as descriptor-storage
borrowing. See the [compiler conformance inventory](../implementation/conformance-gaps.md).
