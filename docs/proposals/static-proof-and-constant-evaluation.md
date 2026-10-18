# Static proof and constant-evaluation follow-up

**Status: the logical proof floor, constant execution, evaluation/effect
contracts and default envelopes are accepted. The physical integration
recommendations and optional extensions below are unapproved.**

## Accepted contracts

The canonical
[proof and constant-execution chapter](../specs/language/proof-and-constant-evaluation.md)
defines scalar facts, predicate transfer, joins, conservative loop handling,
invalidation, evaluator eligibility, deterministic accounting and the portable
resource envelopes. The
[published-predicate and pure-evaluation chapter](../specs/language/published-predicates-and-effects.md)
defines input-state reads/effects, Pure/ConstEval forms, mode queries, NoAlias
and declared guarantees. Those decisions are no longer proposals.

The [specified conformance cases](../specs/conformance/source-proof-and-constant-evaluation.md)
are requirements, not evidence that the current compiler passes them.
[Hosted support](../implementation.md#hosted-compiler) remains a bounded
implementation, not the portable acceptance boundary. Its private evaluator
limits, supported values and proof subset belong in the implementation
inventory rather than a second resource table here.

## Recommended physical integration

| Retained fact | Integration owner and required work |
|---|---|
| Pure/ConstEval author opt-in, ordinary/pure/constant forms and mode queries | [Published evaluation forms](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms-encoding-pending); preserve distinct form bounds and exact registered identities, not a same-named attribute or an overloaded NoThrow bit |
| Bound require/ensure predicates, formal/result/output subjects and read footprints | [Published predicate contract](../specs/language/published-predicates-and-effects.md#published-predicate-subset), [logical records](../specs/representation/logical-records.md); typed graphs and finite roots with phase, revision/invalidation and complete call/witness dependencies |
| Required constant program or admitted intrinsic | [Constant execution admission](../specs/language/proof-and-constant-evaluation.md#dependencies-availability-and-admission); exact selected program/form, transformed inputs, executed dependencies and deterministic resource recipe |
| Frozen target value | [Constant value domain](../specs/language/proof-and-constant-evaluation.md#constant-execution-domain), [target layout](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations); typed values/tags/fields, transitive Blittable commitment and actual value codec, not host object bytes |
| Layout-dependent derived constants | [Registered queries](../specs/language/registered-queries.md); published fixed promise or complete coupled rebuild/rebind/invalidation closure, not merely the scalar result's codec |
| Independent exchange | [Metadata](../specs/il-metadata.md), [registry](../specs/representation/registry.md), [manifests](../specs/representation/semantic-manifests.md), [binding/replacement](../specs/representation/binding-and-replacement.md); exact schema, coherent capability closure, typed remapping and consumer checking |

Recommend sharing typed subject, dependency and callable/witness references
with the [generic/construction proposal](generic-nominal-construction-metadata.md)
and [native metadata proposal](trrm-native-interop-metadata.md), rather than
adding a second predicate language or a literal-only evaluator. The existing
reserved Dependency clause `0x0004` is a candidate shared container, not an
already admitted footprint codec: native provenance, pure read-state
dependencies and constant layout coupling have distinct meanings and phases.
Their complete variants, owner/occurrence rules and composition must be
reviewed together. No footprint variant, predicate opcode, Feature ID,
proof-certificate or constant-program format is assigned here.

A minimal source-only implementation can improve the accepted proof floor
and evaluator without first serializing programs. It cannot claim
source-independent consumption. Conversely, putting Pure/ConstEval bits in
Effects would be a substantive but incomplete redesign: flags do not encode
input-state reads, selected forms, output reservations or program/value
availability. Recommend the shared logical carriers with explicit physical
records before independent exchange; the exact codec remains a blocker.

Bulk intrinsic and literal-parser registration must identify the exact
input transformation, validity/domain, work expansion, live scratch and
result encoding. Repeated work is charged even if cached. A native method
name, private source grant or one cheap helper step supplies none of that
contract. The [registered-string follow-up](registered-string-target-binding.md)
does not establish an alternative source-reading JIT evaluator.

## Conformance gates

Extend the accepted source/evaluation cases through the public compiler APIs:
interval/exclusion joins, saved predicates and copied subjects, mutation and
alias invalidation, conservative loop resets, postcleanup static ensures,
and unknown versus disproved versus exhausted outcomes. Test the default
envelopes at and just beyond every work/depth/state/result boundary using
the specified semantic charges, not the old implementation's step count.

Required execution needs selected-path admission while retaining whole-body
structural/source checks. Include mode-guarded runtime-only work, reached
unmarked calls, fresh init outputs versus writes to pre-existing inputs,
quiet constant NaNs versus bit-preserving sign/copy operations, native-width
target differences, immutable input backing and no escaping evaluator
references. No failed request may fall back to runtime or publish a partial
value.

For physical acceptance, use an independent producer/consumer with no source
or private body lookup. Reject omitted/changed form, footprint, witness,
value/layout or cost dependencies; remap typed subjects and references during
packing. Re-encoding must rebuild affected indexes, maps and source locations,
not substitute bytes. These are acceptance requirements, not executed checks.

## Optional extensions

General relational solving, induction, private-call inference and
alias-sensitive numeric analysis are sound optional extensions, not minimum
floor guarantees. Managed graphs, native resources and identity-bearing
constant values require separately admitted execution and representation
features. Wall-clock cancellation remains a tooling failure, never an
alternative proof or constant result.
