# Module semantic manifests

## 1. Authority and identities

These rules define `trrm-semantic-manifest/1`, the physical manifest for
[requirements and admission](requirements-and-admission.md). The
[registry](registry.md) owns the numeric edition and Feature namespaces.
The artifact still uses one exact encoding schema. A Module's semantic edition
does not select another row layout, opcode catalog or decoder.

The current draft schema is `trrm/1-draft-unified-3`, still format `(1, 0)`.
[Format versioning](format-versioning.md) requires exact out-of-band draft
agreement, not compatibility with an earlier same-pair preview. There is no
new header field, release marker or schema-ID record.

| Field | Identity and domain |
|---|---|
| `Module.governingRuleEdition` | Required exact registered governing edition; nonzero `u32` |
| `Feature.featureId` | Registered coherent optional capability; nonzero `u32` |
| `Feature.ruleRevision` | Exact required semantic revision of that capability; nonzero `u32` |

These three fields use all 32 bits. They are not table references, row indexes,
DefIds, implementation versions or numeric minimums. Zero is invalid, not the
reader's default edition or an implicit revision. Unknown identities can be
reported by structural inspection but cannot satisfy retained semantic use.

## 2. Governing baseline and capability registration

Edition `1`, `bounded-scalar-rules/1`, governs the current independently admitted
scalar interchange baseline. Its interpretation is the existing canonical
metadata, Body, scalar, conversion, effect, failure and admission contracts;
its physical execution domain is the registry's `scalar-copy-failfast` profile.
It requires no extra Feature rows.

This is an interchange baseline, not a replacement for the source
[language floor](../overview/conformance.md#language-floor). All 26 source
scalar identities remain required there. The current nine executable primitive
identities, stated optional values and call restrictions do not acquire richer
codecs merely because the semantic edition is known. A source implementation
and a consumer of this bounded codec remain distinct conformance objects.

Features name coherent optional capabilities, not individual opcodes, APIs or
internal chapter labels. Each registered `(featureId, ruleRevision)` defines:

| Required definition | Obligation |
|---|---|
| Semantic scope | Exact behavior, declarations and observations supplied by the capability |
| Governing editions | The exact editions under which that revision has a defined compatible interpretation |
| Prerequisites | Exact Feature/revision pairs and all required target, runtime, helper and contract dependencies |
| Representation | Every applicable declaration, value, program, operation, phase, owner/root/handler and dependency carrier under each supported encoding schema |
| Admission and transformation | Complete validation, directed binding, rewrite and preservation rules |
| Conformance | Independent positive, negative and transformation cases for the complete capability |

A layer not needed by a capability is explicitly inapplicable, not silently
missing. Allocation, a matching name, a known field shape or a producer's support
claim does not complete a definition. No Feature ID is registered yet, including
the provisional output profile's implementation number.

Edition baselines and Feature prerequisites are supplied by the authoritative
semantic catalog, not by executable producer code. Their exact definitions are
part of draft agreement. Once published as released identities, they are not
reinterpreted; new semantics need a new edition identity or Feature revision.
No numeric compatibility mapping is implied or currently registered.

## 3. Rows, ranges and ordinary encodings

Module is a fixed 48-byte row. `governingRuleEdition` follows `identity`;
the remaining fields keep their meanings. Feature remains an eight-byte row,
`u32 featureId, u32 ruleRevision`, in required section kind `0x0004`.
Header reserved bytes and `moduleFlags` remain zero.

`Module.featureFirst/featureCount` names its explicit Feature rows under the
common checked 24-bit row-range rules. Empty is exactly `(0, 0)`. A nonempty
range requires a present Feature table and fits its actual record count.
The range is not a partition cursor or an artifact-wide union of requirements.
Each emitted Feature row belongs to at least one Module manifest; a complete
structural validation checks that no row is orphaned.

Identical immutable rows or contiguous lists can be shared by Modules. Overlapping
ranges are permitted: each shared row denotes the same exact pair in every using
Module. Each Module independently recovers its own complete list;
sharing does not merge their governing editions, prerequisite closures or
admission decisions. The four existing Module partition cursors retain their
one-based cursor rules, including an end cursor beyond the last row.

Ordinary readers accept reordered explicit lists and repeated identical pairs.
They denote the same exact requirement set. Different revisions of one Feature
within a Module's effective closure are a conflict, not equivalent encodings.
Canonical writers order each explicit list by unsigned Feature ID without
repeated IDs; explicit scoped canonicality audits reject deviations. No uniquely
minimal generating set, globally unique row interning or whole-artifact byte
image is required.

The current catalog has no Feature IDs and edition `1` needs none, so valid
current producers emit no Feature table and use `(0, 0)`. The assigned Feature
codec permits bounded inspection of unsupported requirements; it does not let a
producer choose new semantics or activate an incomplete capability.

## 4. Complete closure and before-use support

A Module's effective requirements contain its exact edition baseline, explicit
Feature pairs and the least complete prerequisite closure. Catalog-derived
prerequisites need not be duplicated in physical rows. Identical obligations
are visited once; finite cycles reach a finite closure. This does not permit
storage, initialization or execution cycles forbidden by their own contracts.

Unknown required identities/revisions, missing prerequisite definitions,
incompatible edition/Feature interpretations and conflicting revisions reject
before affected binding, evaluation, lowering or execution. The consumer supports
each exact requirement or an explicitly applicable directed compatibility mapping.
It never chooses a larger revision, assumes symmetry/transitivity or rewrites the
required identity to the implementation's revision.

Support includes the actual required representation, target, runtime and retained
contracts, not just catalog recognition. Every retained dependency Module keeps
its own manifest, including a producer reached through packed direct references
or non-call obligations. ModuleRef selects that producer; it does not copy its
manifest, impose the consumer's edition or replace complete binding checks.

Trusted selective access checks the header/directory and each accessed Module,
range, Feature word and reference naturally. It admits all retained Module
requirements before affected use but need not scan uncalled Bodies or every
unretained Module's manifest. A structural inspector can report known-shape,
unsupported requirements without executing them. Whole-artifact structural,
semantic and canonicality claims check their separately stated scopes.
Producer validity still includes complete manifests and truthful dependency facts.

## 5. Packing, trimming and decision reuse

Packing retains each Module's exact edition and effective requirements while
remapping Feature ranges and all affected row references. It can share identical
lists or remove exact duplicate rows without changing meaning. Taking a maximum
revision, combining Module manifests into one baseline, or relabeling an edition
is not an encoding transformation.

Trimming removes only genuinely unretained Modules or discharged requirements
under complete preservation rules. Retained value, contract, code/load, cleanup,
owner and root dependencies survive even when a machine call disappears.
Admission reuse depends on the actual schema, manifest, catalog/support mapping,
selected dependencies and target/runtime/trust facts, not a Module name or
producer-authored accepted bit.

## 6. Completion boundary

[Manifest cases and bytes](../conformance/il-semantic-manifests.md) cover these
fields, exact support/closure, scoped access and finite re-encoding. Current
artifact vectors now carry governing edition `1`; all non-Module payloads and
Body instruction bytes retain their previous meanings.

This protocol registers no richer capability. The
[logical-record inventory](logical-records.md) still gates the missing access,
witness, constant/program, handler/cause, phase, dependency, owner and root codecs.
General source checking, arbitrary contract/body truth, native execution and
production transformations require independent evidence.
