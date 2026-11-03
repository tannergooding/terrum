# Aggregate NativeOutput source-dependency applicability

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract workflow.** This remains a separate decision
about applying the accepted DependsOn/ForwardsBacking vocabulary to a proposed
NativeOutput cell. It neither approves the cell contract nor changes
dependency behavior for other destinations.

## Decision and owner

Reuse the existing DependsOn and ForwardsBacking schemas for the complete
inner T published by an eligible NativeOutput formal. The exact candidate
placement, flat source recipes, empty-set/default behavior, broad borrowing
and backing-forwarding meanings, outcome rules, and rejection cases are
integrated in
[Result-correlated NativeOutput cells — Source-dependency applicability](aggregate-native-outputs.md#source-dependency-applicability).
That section is the semantic owner; this document records the distinct
applicability choice and its approval boundary rather than a second full
specification.

The extension selects no new attribute, relationship, phase, record or
feature. It does not reinterpret Dependency rows or dependency source
attributes outside this explicitly proposed destination extension. An
output relationship does not initialize or reserve the destination, validate
the provider's value, or grant ownership; those obligations remain in the
[NativeOutput cell contract](aggregate-native-outputs.md).

## Preserved boundaries and prerequisites

The proposed destination is only the exact formal of an otherwise eligible
NativeOutput cell: `write ptr<T>`, a closed positive-extent directly
interoperable `Blittable Copy` T, and an independently admitted bodyless
foreign role. It denotes the published inner T, not its pointer descriptor,
callee parameter slot, caller destination slot, or reserved output address.
An arbitrary write pointer is not a dependency output.

Sources remain only already-admitted receiver/formal inputs mapped through
the checked call argument map. A NativeOutput formal is not a source; raw
pointees do not acquire checked backing from pointer spelling. The cell's
call-extent no-escape rule still applies. No source rule here adds retention,
pinning, marshalling, resource adoption or release.

Adoption requires the cell contract and its exact selected schema, plus
independently supported backing, provider binding, verification, persistence
and lowering for every required relationship. Private persistence remains
owned by
[aggregate NativeOutput private persistence](aggregate-native-output-private-persistence.md);
foreign-interface identity and direct-import provider binding remain in their
separate proposals. Checked-reference outputs and indirect foreign-call
contracts are not composed into this source-dependency decision.

## Required specification integration

If adopted, amend only the applicable destination/phase and source-schema
rules in
[dependencies and views](../specs/language/dependencies-and-views.md#source-schemas-and-targets),
the corresponding dependency explanation in
[grammar §5.5](../specs/grammar-spec.md#result-dependency-declarations), and
the selected shared Dependency clause/record owners. Preserve their current
rules for all other destinations and exact schema selection for each
interchange format. A private implementation or a successful empty-set case
does not establish this source applicability.

Conformance cases are collected with the cell owner's
[acceptance cases](aggregate-native-outputs.md#proposed-acceptance-cases),
including explicit emptiness, copied descriptors, source-order mapping,
forwarding refinements, missing obligations, and rejection of output-to-output
projection. Current support and remaining implementation gates belong in the
[conformance handoff](../implementation/conformance-gaps.md), not in this
decision record.
