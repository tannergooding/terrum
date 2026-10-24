# Module semantic manifests

## 1. Authority and identities

These rules define `trrm-semantic-manifest/1`, the physical manifest for
[requirements and admission](requirements-and-admission.md). The
[registry](registry.md) owns the numeric edition and Feature namespaces.
The artifact still uses one exact encoding schema. A Module's semantic edition
does not select another row layout, opcode catalog or decoder.

The current draft schema is `trrm/1-draft-layout-queries-1`, still format `(1, 0)`.
[Format versioning](format-versioning.md) requires exact out-of-band draft
agreement, not compatibility with an earlier same-pair preview. There is no
new header field, release marker or schema-ID record.

| Field | Identity and domain |
|---|---|
| `Module.governingRuleEdition` | Required exact registered governing edition; nonzero `u32` |
| `Feature.featureId` | Registered coherent optional capability; nonzero `u32` |
| `Feature.ruleRevision` | Exact required semantic revision of that capability; nonzero `u32` |

These three fields use all 32 bits. They are not table references, row indexes,
DefIds, implementation versions or numeric minimums. Producers must not use
zero or unregistered identities in a valid manifest; zero does not mean the
reader's default edition or an implicit revision. An implementation may
report unknown identities, but they do not establish a support claim.

## 2. Governing baseline and capability registration

Edition `1` governs the accepted TRRM 1.0 semantic baseline. It is not a
reduced scalar execution floor and does not select a separate executable
profile. The full accepted TRRM 1.0 requirements define GA conformance; an
implementation may advertise partial capability support, but cannot claim
full GA conformance while omitting a required contract. Module requirements
still identify the exact semantics a particular artifact retains, and each
consumer admits them only with complete capability closure.

This remains distinct from the source
[language floor](../overview/conformance.md#language-floor): source
conformance and source-free artifact support are separate evidence claims.
Knowing the governing edition does not make an unassigned codec executable.

Features name coherent required capabilities, not individual opcodes, APIs or
internal chapter labels. Native Features `1..7` are registered at exact
revision `1`; they cover native boundaries, rootless values, direct calls,
exports, interface calls, data imports and native exception unwinding. The
prerequisite catalog is authoritative: in particular, native exception
unwinding is a separate capability and is not a prerequisite for ordinary
rootless values, calls, exports or data imports. Feature IDs `9..12` remain
assigned at revision `1`, with their full semantic and representation
registrations pending. Each registered `(featureId, ruleRevision)` defines:

Feature `13=LayoutQueries`, revision `1`, is registered with no unconditional
Feature prerequisites. It covers retained `DE` kinds 1..5, the internal
TypeIdentity carrier/merge/comparison/conditional-selection flow, Body
FieldMap and global FieldPath, FieldDef Import/Export declaration binding,
and retained target-layout field-path dependencies. Other retained
constant, target, witness, runtime or generic-evidence uses still require
their own complete prerequisite closure. The five-map `mapMask` framing is
common schema structure and does not itself require Feature 13.

| Required definition | Obligation |
|---|---|
| Semantic scope | Exact behavior, declarations and observations supplied by the capability |
| Governing editions | The exact editions under which that revision has a defined compatible interpretation |
| Prerequisites | Exact Feature/revision pairs and all required target, runtime, helper and contract dependencies |
| Representation | Every applicable declaration, value, program, operation, phase, owner/root/handler and dependency carrier under each supported encoding schema |
| Admission and transformation | Producer-validity, directed binding, rewrite and preservation rules; consumers validate selected manifest structure, references and retained support requirements before affected use |
| Conformance | Independent positive, negative and transformation cases for the complete capability |

A layer not needed by a capability is explicitly inapplicable, not silently
missing. Allocation, a matching name, a known field shape or a producer's support
claim does not complete a definition. An artifact requiring an incomplete
registration is rejected for affected use and does not establish a
current-schema support claim.

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
common 24-bit row-range rules. Empty is exactly `(0, 0)`. A nonempty
range requires a present Feature table and fits its actual record count.
The range is not a partition cursor or an artifact-wide union of requirements.
Each emitted Feature row belongs to at least one Module manifest; this is a
producer obligation. An optional structural audit may report orphan rows
within its complete scope.

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

The registry assigns native Feature IDs `1..7`, Feature IDs `9..12` for
proof/value/execution capabilities, and Feature 13 for layout queries. IDs
`9..12` still lack complete prerequisite closure and required record schemas.
A module with no required Feature rows
uses `(0, 0)`;
that representation does not redefine edition `1` as a reduced conformance
floor. The assigned Feature codec permits bounded inspection of unsupported
requirements; it does not let a producer choose new semantics or activate an
incomplete capability.

## 4. Complete closure and before-use support

A Module's effective requirements contain its exact edition baseline, explicit
Feature pairs and the least complete prerequisite closure. Catalog-derived
prerequisites need not be duplicated in physical rows. Identical obligations
are visited once; finite cycles reach a finite closure. This does not permit
storage, initialization or execution cycles forbidden by their own contracts.

Unknown required identities/revisions, missing prerequisite definitions,
incompatible edition/Feature interpretations and conflicting revisions do not
establish a valid producer artifact or complete support claim. The
specification does not prescribe consumer validation or handling of such
metadata. A support claim includes each exact requirement or an explicitly
applicable directed compatibility mapping.
It never chooses a larger revision, assumes symmetry/transitivity or rewrites the
required identity to the implementation's revision.

Feature 13 is required when retained content uses any `DE` kind, TypeIdentity
carrier/comparison/conditional flow, a nonempty FieldMap, a FieldPath row,
FieldDef Import/Export binding under Feature 13, or the associated
retained path/dependency representation. Declaration-only FieldDef
Imports/Exports require the binding capability even if no query survives.
An absent or empty-present FieldMap, `mapMask`, metadata-only type recipe or
ordinary fixed-count constant/live Count call alone does not require Feature
13. A fully eliminated closed identity operation requires it only if the
retained closure still uses this capability.

Support includes the actual required representation, target, runtime and retained
contracts, not just catalog recognition. Every retained dependency Module keeps
its own manifest, including a producer reached through packed direct references
or non-call obligations. ModuleRef selects that producer; it does not copy its
manifest, impose the consumer's edition or replace complete binding checks.

Consumers validate the manifest structure and derived closure, and check
support for selected dependency requirements before use. A support claim
includes all retained Module requirements; unretained Modules are outside
that claim. Consumers safely decode the selected Bodies and inspect required
forms/records needed to establish schema and support before code generation
or execution, then rely on producer-published contract truth without
semantic re-proof. An implementation may report known-shape, unsupported
requirements without executing them. Optional audits state their examined
scope. Producer validity still includes complete manifests and truthful
dependency facts.

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
artifact vectors carry governing edition `1`. The current exact draft schema
also assigns its masked Body prefix and layout-query operations; earlier
same-pair previews are not aliases and do not retain those interpretations.

The current draft registers native Features `1..7`, but registration does
not establish compiler or runtime support. Feature 13 is registered for
layout queries and TypeIdentity but still requires implementation support and
its conditional target/generic/constant prerequisites. Features `9..12` and
other richer capabilities still have incomplete schemas or codecs. The
[logical-record inventory](logical-records.md) identifies remaining access,
witness, constant/program, handler/cause, phase, dependency, owner and root
gates. General source checking, arbitrary contract/body truth, native
execution and production transformations require independent evidence.
