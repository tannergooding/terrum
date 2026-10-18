# Compare-exchange order-pair proposal

**Status: the semantic pair matrix and strong-CAS behavior are accepted.
Exact API and physical capability recommendations remain unapproved.**

## Summary

The [memory model](../specs/memory-model.md#explicit-order-semantics) owns the
complete success/failure table; do not keep another pair table here.
[Concurrent executions](../specs/runtime/concurrent-executions.md#6-atomic-outcomes-and-failure-effects)
owns attempt/read/write effects, and the
[library surface](../specs/memory-model.md#library-access-surface) owns the flat
MemoryPrimitives family and defaults. The
[core-library proposal](core-library-reference.md#memoryprimitives) is the
single owner of candidate signatures and the richer observation record.

Recommend keeping explicit success and failure inputs in that API. A new
order-pair wrapper type would change source binding/defaults and require its
own value/constant contract, without changing the accepted nine semantic
pairs. There is no need to redesign the pair domain merely to encode it.
Exact enum/argument spelling, invalid/nonconstant diagnostic identity,
eligible comparison representations, width/alignment/progress support and
separately named weak variants remain review work.

## Physical integration

The [registry](../specs/representation/registry.md) reserves an explicit-memory/
atomic family band but assigns no CAS instruction or atomic Feature. An
implementation must retain both orders, the strong/non-spurious role,
comparison representation, target/runtime dependencies and result contract
through source binding, body verification, persistence and replacement.
An unchecked pair or a machine instruction with weaker failed-read ordering
is not a valid lowering.

Integrate the metadata/operation schema, manifest closure, reader checks and
target capability contract together before source-independent use. Do not
encode these obligations as optional attributes, infer them from an API name
or adopt a new wire enum merely by reusing the proposed source enum ordinals.
No numeric assignment or target support is selected by this follow-up.

## Required conformance validation

When implemented, test all 25 semantic-value pairs against the canonical table,
accepting exactly the nine listed there. Check success/failure read-write
effects and that failure cannot create a release communication edge; include
acquire-failure and sequential-failure litmus cases. Keep strong-CAS outcomes
and defined-representation checks separate from order-pair validation.

Also reject a runtime-selected pair even when one execution chooses an allowed
combination, and reject invalid persisted values/pairs before affected use.
Cover omitted defaults, named-argument evaluation, bit-distinct zeros/NaNs,
unsupported padding, failed-read SC/full-order effects and result-free
rewrites that must not weaken ordering. Packing and compatible replacement
preserve both order requirements; a local source check is not consumer-side
validation or proof of the concurrent outcomes.
