# Artifact-local fixed-array metadata in private revision 100

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract authorization.** This remains a separate projection and
replacement decision for
[private fixed-array persistence](native-fixed-array-private-persistence.md).
It changes no bytes, schema identity, runtime admission or approval status.

## Decision and owner

Recommend artifact-local metadata recipes under the exact candidate
`terrum/private-semantic-100-structural-fixed-arrays-1`. The complete
candidate behavior is integrated into
[Dependencies, structural remapping and replacement](native-fixed-array-private-persistence.md#dependencies-structural-remapping-and-replacement)
and the linked identity/metadata-only sections. Those sections are the
implementation-facing owners; this companion records the independently
unapproved scope decision.

Full persisted modules, sets, graph libraries and source-free execution
artifacts retain the complete closure of their retained implementation
records, including descriptive nested-array, nominal-field, pointer-chain and
function-pointer recipes. Bodyless references and Contract-only projections
are rooted only in existing legally exported runtime contracts and their
required declarations/dependencies. Unrooted metadata-only recipes may be
pruned. A public type name, implementation row or metadata presence is not a
type-only export/import identity; revision 100 adds none.

Replacement preserves every original retained legal expectation and
non-array fact actually relied on, but does not compare unrelated
implementation-local metadata recipes as if they were exported contracts.
Changing/removing a valid unrelated recipe is permitted. An incompatible
provider origin/layout/Copy/access or other non-array fact required by a
retained contract still rejects. Pruning never repairs malformed selected
input, admits an excluded runtime occurrence, or erases an offending export.
A pruned bodyless reference cannot restore implementation bodies/descriptions;
source-free re-emission requires the corresponding full artifact.

## Preserved contracts and approval boundaries

All revision-100 bytes and exact selection remain unchanged: the revision-99
composition, 44-byte array table, tags 43–46, flat direct-local runtime scope,
metadata-only nested/zero-payload exclusions, geometry/cycle/resource checks,
canonical remapping and Standard A2 separation remain owned by the base
proposal. This decision adds no source/API/wire carrier, nominal origin codec,
provider search, resilient layout or compatibility reader. A future type-only
export/import contract would require its own explicit identity, origin,
closure, binding, replacement and consumer decisions.

If adopted, this companion supersedes only the base proposal's broader
interpretation of metadata-only roots and the corresponding projection/
replacement closure rules. It does not weaken full-artifact retention or the
base's rejection of nested-array, ptr-to-array and non-unit zero-payload
runtime operations. Formal approval of this boundary and candidate revision
100 remains outstanding.

The base proposal's
[acceptance cases](native-fixed-array-private-persistence.md#required-tests-and-implementation-dependencies)
cover full-artifact closure, bodyless pruning, malformed-input rejection,
compatible/incompatible replacement and exact remapping. Current implementation
support belongs in the implementation inventories; those cases are not test
results.
