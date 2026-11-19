# Standard TRRM C1 source-provider CLI transport

**Status: unapproved candidate; provisionally actionable under the
user-authorized missing-contract workflow, pending later review.** This
selects a development CLI input agreement, not normative adoption,
implementation support or qualification.

## Missing decision and recommendation

Recommend an explicit **version-2 pairing of the existing
`compile-trrm --trrm-source-providers <path>` option** with the finite
class/type schema set below. Retain its ordered artifact vector and explicit
Module/type/field selections. Version 1 remains exactly the nested-FieldPath
[transport](standard-trrm-source-provider-cli-transport.md); neither version
is a compatibility alias for the other.

The distinction is consumer binding versus local publication. The
[class-source publication file](standard-trrm-c1-class-source-cli-transport.md)
continues to contain no provider fields. Permit the two files together only
when each independently admits the explicitly selected schema and its own
configuration version. An output export map never selects an input provider
or supplies a missing provider key.

The gap is not inferred from an inventory entry:

| Inspected owner | Actual boundary |
|---|---|
| [`Program.TrRmExecution`](../../sources/terrumc/Program.TrRmExecution.cs) | Both the option gate and every embedded provider selection require exactly `trrm/1-draft-init-reference-bodies-local-field-layout-2`. The route calls the five-argument reference factory even with an empty field list. Artifact `--trrm-provider`/`--trrm-module` options are unavailable on `compile-trrm`. |
| [`TrRmSourceProvidersReader`](../../sources/terrumc/TrRmSourceProvidersReader.cs) | Accepts only exact number token `1` and the closed `providers`/`references` shape. The current selector record is named `TrRmSourceProviderReference`, with ordinal, Module, type-key and field-key fields; there is no inspected `TrRmSourceProviderSelection` API/model supplying a separate class route. |
| [`CreateStandardReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs) | The four-argument explicit-type overload has bounded C1M1/CM1/EN1 paths. The five-argument explicit-type/field overload has LayoutQueries, nested-FieldPath and EN1 paths, not general constructor-family applicability. API availability is not CLI admission. |
| [`CreateStandardReferenceSet`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs) | Takes two or more references but rejects class/interface/enum/generic inventories and accepts only exact LayoutQueries or nested-FieldPath associations. A whitelist-only CLI change cannot implement class composition. |
| [Class-source CLI proposal](standard-trrm-c1-class-source-cli-transport.md#exact-cli-and-schema-admission) | Explicitly selects no provider fields or composition with the nested-only consumer option. The [publication owner](standard-trrm-c1-class-source-publication.md#expectedactual-binding-and-complete-comparison) requires explicit class roots and complete original provider views, but assigns no consumer CLI grammar. |
| Active [source-provider](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.TrRmSourceProviders.cs) and [generic-map](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.TrRmGenericMethodExports.cs) fixtures | Source-provider positives use the nested literal; the generic-map fixture explicitly rejects C1-family pairings. These are inspected test sources, not executed results. |

[Implementation](../implementation.md) and the
[gap index](../implementation/conformance-gaps.md) distinguish those bounded
APIs from missing CLI work; they do not supply the missing pairing.

This proposal adds the consumer pairing and narrowly supersedes the
source-provider proposal's nested-only applicability **for version 2 only**,
and the class CLI proposal's no-composition statement **for independently
eligible version-2 consumer inputs only**. All publication rules and existing
version-1 pairings remain unchanged. No accepted specification is replaced.
Preserve [source input](../specs/language/source-input.md),
[exact draft agreement](../specs/representation/format-versioning.md),
[binding/replacement](../specs/representation/binding-and-replacement.md)
and [consumer checking](../specs/representation/reader-trust.md).
No TRRM ID, Feature, record, source syntax, runtime agreement or
[`terrum.cli/1`](../specs/cli-protocol.md) field is assigned.

## Exact candidate pairings

Require explicit `--trrm-schema` selection. Version 2 admits **exactly these
fifteen existing candidate literals**, compared as complete ordinal,
case-sensitive strings:

| Explanatory name | Exact schema identity |
|---|---|
| C1M1, metadata-only | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1` |
| Construction-1L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1` |
| Construction-2L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1` |
| Construction-1L1R1PI1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1` |
| Construction-2L1R1PI1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1` |
| Construction-1L1R1 EN1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1` |
| Construction-2L1R1 EN1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1` |
| Construction-1L1R1PI1 EN1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1` |
| Construction-2L1R1PI1 EN1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1` |
| Construction-1L1R1 EN1 class layout | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1` |
| Construction-2L1R1 EN1 class layout | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1` |
| Construction-1L1R1PI1 EN1 class layout | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1` |
| Construction-2L1R1PI1 EN1 class layout | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1` |
| CM1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1` |
| CM1AR1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1` |

These are the existing [class](standard-trrm-c1-class-source-publication.md),
[receiver/constructor](standard-trrm-c1-class-source-cli-transport.md),
[enum](standard-trrm-c1-enum-closure-composition.md),
[class-layout](standard-trrm-en1-class-field-layout.md),
[CM1](standard-trrm-c1-class-parameter-defaults.md) and
[CM1AR1](standard-trrm-cm1-associated-relation-constraints.md) agreements,
not new combinations. The table is closed: a future successor or extension
of a linked owner's table is not automatically admitted here.

Every provider, including unused and declaration-only inputs, must explicitly
select **the identical literal selected for the consumer output**. Retained
origins and captured Clause/Origin/Plan artifacts must satisfy that same
agreement before schema-dependent access. No prefix/suffix parsing, component
switches, Feature/row sniffing, schema conversion, relabeling or retry exists.

Version 1 still pairs only with exact local-field-layout-2; version 2 does not
pair with it. Reject Development/default selection, LayoutQueries, original
C1, declaration-only label/receiver siblings, label-free construction parents,
G1, private inputs and every other unlisted literal, even for empty fields or
an enum-free/class-free provider. JSON version selects the configuration
agreement, not an artifact decoder.

This consumer table does **not** broaden a publication option's pairings.
In particular, C1M1 does not acquire the constructor-family class export
file, and CM1AR1 does not acquire an associated-type source-map grammar.
An otherwise admitted consumer can omit local mapping files; required local
publication closure must still be representable. Existing class-map versions
and their explicitly selected extensions remain independently governed.

## Exact CLI and JSON shape

The option retains its existing spelling, one operand and placement:

```text
compile-trrm <source> [--source <source> ...] --identity <using-module> --trrm-schema <one-exact-literal-above> --trrm-source-providers <path> [--trrm-source-class-exports <independently-eligible-path>] [--library] -o <output.trrm> --runtime fail-fast [--json]
```

The brackets describe argv shape, not a new command parser. The required
source precedes options; `--json`, when present, remains final. Reject
repetition, missing/empty or dash-leading operands, `-`, alternate spellings,
equals/short aliases and other commands before configuration reads.
Keep the current identity, arithmetic, fail-fast, target requirement **1**,
output-extension, library/entry and maximum **256** ordered source-input gates.
Artifact `--trrm-provider`, `--trrm-module` and anchor options remain unavailable
on `compile-trrm`; no artifact-command schema whitelist changes.

A complete candidate file follows. The paths, Modules and keys illustrate
input structure, not validated artifacts:

```json
{
  "schemaVersion": 2,
  "providers": [
    {
      "schemaIdentity": "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1",
      "path": "backing.trrm"
    },
    {
      "schemaIdentity": "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1",
      "path": "facade.trrm"
    }
  ],
  "references": [
    {
      "providerInputOrdinal": 1,
      "moduleIdentity": "example.facade",
      "typeExportKeys": ["type.facade"],
      "fieldExportKeys": []
    },
    {
      "providerInputOrdinal": 0,
      "moduleIdentity": "example.backing",
      "typeExportKeys": ["type.cell", "type.cell.alias"],
      "fieldExportKeys": ["field.cell.value", "field.cell.value.alias"]
    }
  ]
}
```

Version 2 has exactly the existing three root properties. Provider objects
have exactly `schemaIdentity`, `path`; reference objects have exactly
`providerInputOrdinal`, `moduleIdentity`, `typeExportKeys`, `fieldExportKeys`.
All are required and nonnull. Both root arrays and every type-key list are
nonempty; a field-key list may be empty. Object order is insignificant;
all array order is retained. Reject repeated
`(providerInputOrdinal, moduleIdentity)` entries and repeated keys within or
across one reference's key lists. Distinct keys may alias one definition;
equal key text in different Modules is not a collision or equal identity.

Retain the original transport's strict regular-file UTF-8/JSON rules:
at most one leading signature; exactly one root object and trailing JSON
whitespace only; depth at most **128**; no malformed UTF-8, unpaired decoded
surrogates, duplicate decoded properties at any depth, unknown/missing
properties, nulls, comments, trailing commas/content or wrong types.
Escape-equivalent property names are duplicates. Here `schemaVersion` is
exactly number token `2`, not `2.0`, `2e0`, a string or another version.
Ordinals are unsigned decimal integer tokens without signs/fractions/exponents,
fit `Int32` and are below the provider count. Check all counts, cumulative
allocations and metadata capacities without truncation or partial admission.

Strings are nonempty, NUL-free scalar text, compared decoded ordinally
without trimming, normalization or case folding. Keys are opaque and preserve
whitespace-only spellings; each strict UTF-8 key is **1..`0xFF_FFFF` bytes**.
Resolve both the option operand and embedded paths against the invocation
working directory, not the manifest directory. Embedded paths are local
regular-file inputs, never `-`, dash-leading paths, stdin, URLs, includes,
globs or response files. No environment expansion or discovery is added.

There are no source assertions, callable-root lists, export assignments,
Core roles, checked facts or phase-map overrides in this file. Source
assertions and hashes remain in the independently eligible publication file.

## Admission and API handoff

1. **Argv and configuration preflight.** Check command/option/schema eligibility
   and argv-known aliases before configuration content reads or destination
   acquisition. Read selected configuration files once in argv option order.
   Validate their complete shapes, version/schema pairs, provider selections,
   ordinals, source-path assertions and existing Core composition/using-Module
   agreement before any provider or source content, including stdin.
   Once the provider file's version is known, reject a wrong pairing before
   artifact acquisition; do not try its other version.
2. **All-path identity.** Before content acquisition at each available boundary,
   use existing [`PathsAlias`](../../sources/terrumc/Program.PathIdentity.cs)
   checks over source, configuration, embedded provider and output paths.
   Require distinct file inputs across those roles, including duplicate source
   aliases and duplicate provider aliases. Several reference entries selecting
   distinct Modules in one vector element do not duplicate that input.
   Preserve relative/case/link/Windows hard-link recognition; failed identity
   inspection is an input failure, never proof of distinctness.
3. **Complete supplied set.** Capture provider bytes once in vector order into
   owned immutable snapshots. Validate every supplied artifact and all its
   Modules, not just selected roots or reached dependencies. Build one
   unambiguous Module directory: duplicate Module identities across artifacts
   reject even for equal bytes at distinct paths. Validate retained imports,
   exact schema/support, edition/Features, target/runtime and binding closure
   before exposing a reference. Provider presence supplies neither Core
   authority nor missing phase/evaluation context. Required consumer checking
   does not include semantic re-proof of producer Bodies.
4. **Explicit reference construction.** In reference-array order, use the
   selected vector element as `input`, the exact selected Module, and **all
   other vector elements in original order**, omitting that element once,
   as `providers`. Version 2 with an empty field list uses the existing
   four-argument `CreateStandardReference(input, moduleIdentity, providers,
   typeExportKeys)`; a nonempty field list uses its existing five-argument
   overload with that list. This is specified dispatch, not retry after an
   error. Never use the three-argument all-declarations overload to bypass
   explicit roots. Version 1 retains its unchanged always-five-argument
   handoff. Extending exact applicability and complete closure in these
   factories is prerequisite implementation, not evidence that they work now.
5. **Composition.** Use one reference directly; pass two or more in manifest
   order to the existing `CreateStandardReferenceSet(references)` signature.
   Its required class/type extension must reconcile checked actual nominal
   identities while retaining every original expected/provider view and
   dependency edge. It must not concatenate bound tables, discard unsupported
   inventories, replace expectations with the actual declaration, or reduce
   class references to today's struct-only association. Complete reference
   admission/composition before reading sources.
6. **Single source binding/publication.** Acquire the positional source followed
   by repeatable `--source` operands once under `source-input/1`. Preserve this
   immutable syntax-tree vector, source identities and UTF-16 coordinates.
   Validate selected publication-file hashes against those captured snapshots;
   bind once with the reference through `Compilation.Create` or `CreateLibrary`
   and the existing options. Resolve all local maps against that same
   reference-identical checked program. Use only its independently eligible
   emitter handoff; neither provider keys nor imported expectations become new
   local export assignments. Do not reread, reorder, concatenate or rebind for
   another list.
7. **Atomic publication boundary.** Finish source checks, complete closure,
   artifact construction and output verification before acquiring the output.
   Recheck every source/configuration/provider versus output alias immediately
   before the existing same-directory staged
   [`WriteArtifact`](../../sources/terrumc/Program.Compilation.cs) replacement.
   Prepublication failure preserves existing bytes and creates no absent
   destination; retain staged-file cleanup and no success response on failure.
   Captures are immutable after acquisition, not an atomic transaction across
   changing files, authentication or a new hostile-filesystem/crash protocol.

Retain the class publication file's incompatibility with separate enum/type/
generic mapping options, even for empty maps. A version-2 provider file adds
no exception to those rules or to any map's schema gate.

## Selection, retained views and semantic limits

Each type key selects one public provider-local TypeDef in the exact selected
Module and a nominal kind admitted by that schema's projection owner. C1M1
retains its bounded nongeneric class-root, metadata-only meaning; dependency
structs do not become independent lookup roots. Other entries retain their
existing constructor/enum/generic/interface scope, not an all-TypeDef
projection invented by the CLI.

Every newly selected field key must resolve to one public provider-local
FieldDef in that same Module whose exact owner is among **that reference
entry's explicitly selected type roots**. A hidden layout/inline dependency
does not select its fields. An external declaring Module needs its own entry
and explicit owner/field keys. Imported proxies, names, coincident rows,
matching layouts and provider presence cannot substitute for these selections.
Preserve already-retained exact provider-owned Import/closure associations;
do not manufacture keys or require re-selection to erase an old obligation.
Factory enforcement owns this scope; CLI lookup/rebinding is not a substitute.

For new uses, retain explicit first-alias order through reference entries and
each type/field list. Existing Imports keep their original keys and alias
equalities; order cannot resolve ambiguous names, conflicting revisions or
incompatible contracts. Deduplicate only one checked actual nominal identity,
not independent expectations that happen to resolve to it.

Reference composition and emission must retain original using/provider
snapshots, exact Modules, schema and owner/formal/substitution contexts.
Compare complete normalized types and all retained declaration, field/Layout,
mutability, ownership, access, unsafe/effect, dependency, constructor I/P,
completion, property and witness obligations under their owning contracts.
CM1 defaults and labels remain view-owned: a provider's new source defaults
do not overwrite the consumer's retained omissions or plans. Keep independent
phase-1/phase-2 maps and original Clause/Origin/Plan contexts. Type-root
selection cannot trim required members, implementations or private closure;
it adds no independent free-function/generic-callable selector.

Meaning-preserving composition/re-emission rebuilds affected type, signature,
owner, Import/Export, field/Layout, generic/constraint, contract and Body maps
with their schema-owned references. Row numbers and source names are not
cross-snapshot identity. Preserve every expected-to-actual obligation after
direct resolution or compatible row relocation, including a later failing
edge behind a shared dependency.

This is transport, not permission to execute a class constructor, transfer
unknown ownership, invent GC backing, evaluate a query under the wrong
layout agreement or activate a runtime. C1M1 gains no constructor/member
publication or active Transfer contract. Construction-1 gains no
Construction-2 lifecycle. EN1 does not acquire numeric class layout without
its selected class-layout successor. Required unsupported codecs, contexts,
proofs, provider comparisons or lowering remain explicit refusal gates,
not defaults, erased facts or weaker-schema fallbacks.

## Failures

Keep the existing human and experimental `--json` projections:

| Failure | Candidate projection and boundary |
|---|---|
| Command/option/operand errors, absent/unlisted output schema, forbidden map combination | `TRM2000`, exit 2, before configuration content reads. |
| Known configuration version paired with the wrong exact schema, or any embedded provider selecting a different literal | `TRM2000`, exit 2, after necessary configuration decoding but before provider/source content reads. |
| Configuration/provider/source read or strict-decoding failure; malformed JSON/version/shape/text/count/ordinal; duplicate reference/key; attributable missing/wrong-kind key or explicit owner-selection violation | `TRM2001`, exit 2. Identify file and JSON entry/field where attributable. Preserve existing source-input details without fabricating a UTF-16 span for invalid bytes. |
| Newly checked configuration/provider/input aliases | `TRM2002`, exit 2. Existing source/output argument failures retain `TRM2000`. |
| Source syntax/binding diagnostics | Existing source diagnostics, exit 1, with actual source identities/spans. |
| Invalid/unsupported metadata or required closure; missing selected Module/dependency; duplicate/conflicting Module snapshots; incompatible original view; unsupported composition, Body, proof or context | Existing `TRM5001`, exit 1, preserving existing explicit capacity/resource distinctions. No partial reference/artifact, provider discard or schema retry. |
| Output publication I/O/access failure | Existing `TRM2001`, exit 2; no success response. |

Distinguish a valid field key supplied without its explicit owner selection
from malformed serialized FieldDef ownership: the former is attributable
configuration, the latter invalid provider metadata. Translate only input-
attributable argument failures, not arbitrary compiler `ArgumentException`s
or unexpected host failures. Do not import A3-only admission/resource codes.
All prepublication failures retain the output-preservation rule above.

## Implementation dependencies and focused acceptance

Implementation owners are `Program` usage/configuration/preflight and its
existing strict reader/selector records; the two explicit-root factories;
class-capable checked reference composition; and the existing source-binding,
verification and exact-schema emission handoffs. No new public selector
record or API signature is selected by this transport. In particular, adding
the fifteen strings to a whitelist without implementing those handoffs is
not completion.

Prerequisites remain with the linked class publication, constructor, enum,
property, receiver, query/layout, generic/proof and CM1/CM1AR1 owners.
Publication files supply no missing provider/phase/context codec. This
proposal does not resolve an independently missing representation by putting
unchecked declarations or authorization in JSON.

Required future regressions through CLI/public APIs:

| Family | Required cases |
|---|---|
| Exact pairing/isolation | Every listed version-2 literal; version-1 nested behavior unchanged. Reject both cross-version pairings, omitted/case-altered/parent/sibling/private selectors, and a wrong-schema unused provider before provider/source/stdin content. Keep independent publication-map versions and G1/artifact options unchanged. |
| Basic source handoff | Single/multiple sources, executable/library, no local map and an independently eligible combined class map. Bind selected non-first providers and co-located Modules without rereading artifacts; preserve the full source vector, map hashes, UTF-16 spans, partial declarations and one checked program. |
| Owner/type/field scope | Type-only and explicit field selections, aliases, wrong Module/kind/owner, external owners and hidden inline dependencies. An unselected field/owner cannot be promoted by provider presence; a retained exact Import is not silently rebound. C1M1 remains type-only and unsupported complete constructor-family closure refuses. |
| Complete supplied validation | Corrupt, unsupported, dependency-incomplete, declaration-only and disconnected providers; duplicate Module identities even at distinct paths with identical bytes; conflicting revisions and expectations. An earlier successful reference cannot skip a later failing supplied input. |
| Composition/order | Class/struct/enum dependencies where admitted, shared-Leaf diamonds and multiple expected views of one actual declaration. Preserve first explicit aliases for new uses and original aliases for retained Imports. Reverse selection order: incompatible views still reject, while compatible identities retain complete closure. |
| Cross-feature views | Mutable/immutable classes, constructor I/P and completion, PI1 backing/init facts, EN1 nominal identity, class-layout symbolic dependencies, labels, CM1 expected/provider default differences and CM1AR1 retained generic constraints. Check only each selected owner's scope; transport never invents missing facts or runtime authority. |
| Replacement/remapping | Consume emitted artifacts without source/maps; replace providers with compatible TypeDef/FieldDef/member-row relocation, re-emit/reload and preserve all original expectations, keys and context maps. Changed type/owner/access/mutability/guarantee or incompatible provider-owned dependency rejects, including a later diamond edge. Fresh source recompilation is not replacement evidence. |
| Strict input and atomicity | Invalid UTF-8/surrogates/signatures, escaped duplicate properties, wrong/unknown/null fields, signed/fractional/exponent/version tokens, ordinal and capacity boundaries. Exercise relative/case/link/hard-link aliases, duplicate inputs and late output aliasing. For each failure family assert diagnostic/status, no stdin acquisition before its gate, existing-output bytes, absent-output preservation and no success response. |

These are future implementation gates, not executed checks or evidence of
compiler/runtime qualification.

## Alternatives and remaining decision

| Alternative | Disposition |
|---|---|
| **Existing option, explicit version 2 with the same vector/reference shape** | **Recommended.** One consumer-input mechanism and shared preflight; separates the new reference/schema agreement from exact nested-only version 1 without adding an option or changing publication files. |
| Separate `--trrm-source-class-providers <path>` | Concrete alternative: nonrepeatable, mutually exclusive with `--trrm-source-providers`; exactly the JSON shape above but `schemaVersion` token `1`, the same fifteen literal pairings and all the same obligations. Preserves the old route untouched, but duplicates an externally visible option/configuration agreement for identical inputs and still requires the same compiler composition work. Not selected. |
| Explicitly widen existing version 1 | Possible for an unpublished draft; no compatibility requirement forbids it. Not recommended here: it would make one version name both today's always-five-argument nested contract and the new class/type dispatch/composition contract. Version 2 makes that distinction explicit; it is not a migration reader. |
| Reuse artifact provider switches or embed providers in the class export file | Does not express the same ordered Module/type/field selections without another grammar, and conflates independently owned consumer/publication inputs. Not selected. |

**Remaining user decision:** formally accept or reject the recommended
version-2 pairing and its independently eligible class-map composition,
rather than the separate-option alternative. Under the supplied authorization,
the recommendation is usable as provisional direction; it is not approved.
No other CLI policy choice is intentionally deferred here. Implementation,
the owning semantic/runtime prerequisites and qualification remain separate.
