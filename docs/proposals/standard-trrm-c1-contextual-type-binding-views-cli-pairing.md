# Standard TRRM contextual TypeDef binding views: version-3 CLI pairing

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow, pending later review.** This proposes only a
development CLI pairing amendment. It does not formally approve its dependent
schemas, amend normative specifications or claim implementation/qualification.
Preparation used static source inspection; the active build/test hold remains.

## Finding and recommendation

**An explicit amendment is needed. Extend the existing version-3 pairing table
by exactly the three contextual binding-view successors below; keep the
manifest shape and dispatch unchanged.**

The [method/type CLI transport](standard-trrm-c1-method-type-source-provider-cli-transport.md#exact-pairing-and-impact-boundary)
selects only three base Copy-evidence schemas. The
[contextual binding-view proposal](standard-trrm-c1-contextual-type-binding-views.md#exact-c2-representation-candidate)
selects three whole-schema successors but expressly leaves CLI applicability
unselected. In current
[`Program.TrRmExecution`](../../sources/terrumc/Program.TrRmExecution.cs),
`IsMethodTypeSourceProviderSchemaIdentity` recognizes only the base literals.
It controls both the argv-known source-provider gate and the version-3
post-manifest gate. A successor therefore fails the former before configuration,
provider or source content is read.

The current
[`TrRmSourceProvidersReader`](../../sources/terrumc/TrRmSourceProvidersReader.cs)
already reads the strict version-3 combined-reference shape. Its
`schemaVersion` describes JSON selection grammar, not the artifact encoding.
Existing `--trrm-schema` and provider `schemaIdentity` strings can select the
new exact agreements without another version or field.

This candidate supersedes the transport proposal's **base-only version-3
pairing restriction**, and supplies the contextual proposal's missing CLI
applicability decision, only for this route. Their other rules retain their
owners. Existing base pairings are deliberately retained, not treated as
compatibility aliases. No version-1/2 agreement is amended.

Leaving version 3 unchanged remains usable for base-schema inputs, but does
not transport the selected successor capability. Emitting required equality
under a base identity, ignoring the equality or relabeling bytes would change
that capability or guess a schema agreement. Direct API use alone does not
fulfil the CLI scope. A version 4 would duplicate an unchanged grammar without
resolving any additional selection ambiguity.

## Exact version-3 agreement

The complete admitted version-3 output-schema set becomes these **six full
literals**, consisting of the three retained bases and exactly three additions:

| Agreement | Exact output/provider schema identity |
|---|---|
| Retained Construction-2 + Copy | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1` |
| Retained CM1 + Copy | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1` |
| Retained CM1AR1 + Copy | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1` |
| Added Construction-2 + Copy + binding views | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1-type-binding-views-1` |
| Added CM1 + Copy + binding views | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1-type-binding-views-1` |
| Added CM1AR1 + Copy + binding views | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1-type-binding-views-1` |

Selection rules:

1. `--trrm-schema` must explicitly choose one listed literal. Every
   `providers[i].schemaIdentity`, including the method-root artifact and
   unused/declaration-only providers, must equal it by decoded **ordinal,
   case-sensitive full-string comparison**. Retained origin snapshots must
   remain under the same agreement, as required by the combined operation.
2. Reject every unequal pairing: base/successor in either direction,
   different successor families, and different base families. The presence
   or absence of equality rows never relaxes this rule. A successor artifact
   needing no equality rows still selects the successor, not its base.
3. No prefix/suffix recognition, trimming, normalization, case folding,
   Feature inference, header-version inference, schema overlays, conversion,
   decoder retries or compatibility aliases are selected. Reject omitted,
   Development and all unlisted output selections; an unlisted provider
   identity cannot match an admitted output.
4. Preserve version 1's exact local-field-layout-2 pairing and version 2's
   existing closed fifteen-literal table. Neither acquires these bases or
   successors. Their valid shapes, factories and composition remain unchanged.
   The outer argv gate admits the union of the unchanged old tables and this
   six-literal table; only the decoded manifest version selects its pairing.

The contextual proposal continues to own successor semantics, including
`TypeDefBindingEquality`, distinct expectations, consumer-owned equality,
replacement and remapping. This document allocates no section, Feature,
schema identity, opcode, API or runtime policy. The accepted
[format-versioning](../specs/representation/format-versioning.md),
[admission](../specs/representation/requirements-and-admission.md),
[binding](../specs/representation/binding-and-replacement.md) and
[consumer checking](../specs/representation/reader-trust.md)
contracts remain unchanged. This scoped route's identical-schema requirement
is not a new interchange-wide prohibition on separately selected schemas.

## Unchanged transport and capability boundary

Retain the exact numeric token `schemaVersion: 3`, required root properties
`schemaVersion`, `providers`, `references`, and exactly one reference with
`providerInputOrdinal`, `moduleIdentity`, `methodExportKeys`, `typeExportKeys`,
`fieldExportKeys`. Methods remain a nonempty ordered string list; type/field
lists remain required, possibly empty, ordered Module/key objects.
All strict UTF-8/JSON, duplicate-property, text, ordinal, count and path rules
remain those of the transport owner. Add no equality flag, schema field on
the reference, capability discriminator or optional property.

Use the same single six-argument `CreateStandardReference` call: the selected
artifact/Module supplies the method root, all other artifacts retain vector
order, and every key list retains submitted order. No key inference, callable
reference-set composition or per-Module projection loop is added.

Only the existing classless method/Copy-struct domain is transported. The
successor must preserve original contexts and equality through binding,
verification, emission and source-free reconstruction; transport cannot
substitute a base emitter or discard an unsupported obligation. Other schema
domains, publication/export maps, Core authority, artifact execution/build
commands and their whitelists gain no pairing. Language syntax, target ABI,
ownership, lowering eligibility and runtime policy are not widened.

## Failure ordering and publication

Preserve the transport owner's
[failure contract](standard-trrm-c1-method-type-source-provider-cli-transport.md#failure-contract)
and existing human/experimental `--json` responses, not a new
[`terrum.cli/1`](../specs/cli-protocol.md) envelope.

1. Check argv-known option/schema/map errors and path aliases at their
   existing preflight points. Do not read a manifest to rescue an unlisted
   output selection. Capture configuration files in argv option order.
2. Strictly decode the selected manifest's complete version-specific shape
   before pairing. Malformed JSON/text/shape is not repaired or reclassified
   by a recognizable successor identity. A structurally valid version-1/2
   manifest with a successor output instead fails its unchanged pairing.
3. Check the output/version agreement and **every provider identity** before
   filesystem resolution/inspection of embedded provider paths or reading
   provider/source content, including stdin. Textual path validity remains
   part of strict manifest decoding. A mismatch in an unused provider fails
   here, even when its file is missing or corrupt.
4. After successful pairing, retain path/alias checks, regular-file checks
   and once-only ordered provider capture. Complete combined-reference
   admission before source acquisition. Check every retained contextual
   expectation and equality obligation; an earlier successful view, unused
   callable or unselected Module cannot hide a later failure. Equality never
   supplies a missing key or makes an incompatible contract pass.

| Condition | Existing projection retained for the amended route |
|---|---|
| Missing/unlisted output selection or forbidden argv combination | `TRM2000`, exit 2, before configuration reads |
| Valid manifest with wrong version/output pairing or any unequal provider schema | `TRM2000`, exit 2, before provider/source content reads |
| Strict manifest failure, provider path/read failure or input-attributable selector failure | `TRM2001`, exit 2, with file/JSON selection context where attributable |
| Newly checked input/configuration/provider/output aliases | `TRM2002`, exit 2; existing source/output argument errors retain `TRM2000` |
| Malformed/unsupported successor metadata or incompatible retained binding/equality | Existing `TRM5001`, exit 1; preserve existing capacity/resource distinctions and do not import A3-only projections |
| Source failure | Existing source diagnostics and spans, exit 1 |
| Output I/O/access failure | Existing `TRM2001`, exit 2; no success response |

Construct and verify the entire output before staging it. Recheck all
source/configuration/provider-to-output aliases immediately before the existing
same-directory
[`WriteArtifact`](../../sources/terrumc/Program.Compilation.cs)
publication. Every prepublication failure preserves an existing destination's
bytes and creates no previously absent destination. Retain staged-file cleanup
and report publication failure rather than success. This adds no multi-file
transaction, hostile-filesystem guarantee or crash-durability protocol.

## Implementation consequences and future public cases

Successor codecs, contextual reference binding, equality checking and
`EmitStandard` preservation from the contextual proposal are prerequisites,
not consequences proved by adding CLI literals. Qualification remains open.

* In `sources\terrumc\Program.TrRmExecution.cs`, extend only the version-3
  literal predicate and its already shared outer/post-manifest use. Keep
  ordinal provider/output comparison, ordering, dispatch and old gates.
* `sources\terrumc\TrRmSourceProvidersReader.cs` needs no grammar or selection
  storage change for this amendment. Do not add a second decoder or relaxed
  successor path.
* Extend the public CLI fixtures in
  `tests\Terrum.CodeAnalysis.Tests\Cli\CliTests.TrRmSourceProviders.cs`.
  Construct exact-schema providers through public compiler/metadata APIs;
  no new provider-publication CLI route or friend assembly is required.

Required cases after the hold ends:

| Case | Required observation |
|---|---|
| Exact positive pairing | Parameterize all three successors and retain the three base cases. Exercise scalar-only empty type/field lists and supported method/struct/field consumers, with library and already-admitted executable compilation. Inspect output using the explicitly selected schema, not only status or file existence. |
| Contextual capability | CLI-compose distinct method-root expectations for one actual type and a nested shared leaf required in several using Modules. Include a cross-view source use, non-first/co-located method root and reversed alias order. Inspect distinct original contracts/keys and consumer-owned equality in output; no duplicate source nominal type. |
| Pairing refusal/order | Base/successor mismatch both ways, cross-family successors, case changes and unlisted siblings; include a mismatched unused provider with a missing path and stdin held open without bytes using the existing supervised CLI runner. Require `TRM2000`/2 before provider/source acquisition, without fallback. |
| Version/grammar isolation | Valid old-version shapes with successor selection fail pairing; version-3 fields in old shapes, noncanonical `3.0`/`3e0`/string versions and added equality properties fail strict decoding. Existing version-1/2 dispatch and map/command exclusions remain intact. |
| Binding failure/order | Correctly paired successor artifacts with malformed equality references, an unsupported required form, a missing retained dependency or a group split into individually satisfying equal-layout actuals refuse before source acquisition. Include a later incompatible and an uncalled retained member; no weakening or provider discard. |
| Source-free retention | Load/re-emit/reload CLI output through the admitted public API path without source or manifests. Preserve contextual views, equality owners/endpoints and mapped signatures/fields under compatible row relocation; reject replacement splitting retained equality. Emission/reconstruction is not execution evidence. |
| Output preservation | For pairing, strict-input, path-alias, admission and source failures, check sentinel bytes at an existing output and noncreation of an absent output. Retain late-alias, staging/publication failure, cleanup and no-success-response cases. |

These are implementation acceptance cases, not tests run for this document.
No build, test, native execution or process/profile inspection is authorized by
this proposal; the current hold and separate qualification gates remain.

## Remaining decision

The formal decision is whether to adopt this **explicit six-literal version-3
table with unchanged grammar and exact all-provider/output pairing**. Under
the supplied workflow, that recommendation is provisional direction for
authorized continuation. No additional in-scope transport policy is left
undecided; successor implementation, qualification and later formal review
remain outstanding. No existing proposal, specification, inventory, plan or
work queue is modified here.
