# Standard TRRM source-provider CLI transport

**Status: unapproved candidate; provisionally actionable under the
user-authorized missing-contract workflow, pending later review.** This is
a proposed development CLI contract, not normative adoption, implementation
evidence or qualification.

## Decision and boundary

Add one distinct, nonrepeatable `compile-trrm` option:

```text
--trrm-source-providers <path>
```

Its strict version-1 JSON file supplies already-produced Standard artifacts
and explicit type/field reference selections **for binding consumer source**.
It is not a provider-source build recipe, another `--source` list, an export
assignment file or a dependency resolver.

Initially require explicit `--trrm-schema` selection of exactly
`trrm/1-draft-init-reference-bodies-local-field-layout-2`, and that same
explicit literal on **every** supplied artifact, including unused providers.
This is the bounded [nested FieldPath successor](standard-trrm-init-reference-nested-field-destinations.md),
not a schema-family or prefix match. Its closed nongeneric Copy/layout,
initialization, provider and Body limits remain unchanged.

Keep the [source type/field export file](standard-trrm-source-type-cli-transport.md)
and [class publication file](standard-trrm-c1-class-source-cli-transport.md)
unchanged and publication-only. The new option can accompany the former to
publish eligible **local** declarations while importing selected provider
types. It does not add provider properties to either file or select any C1,
class, enum or generic-method transport pairing. Existing incompatible
publication-option combinations still reject, even for empty maps.
`--trrm-provider`, `--trrm-module` and artifact-only anchor options remain
unavailable on `compile-trrm`; their artifact-command meanings do not change.

### Inspected handoff

These are source observations, not results from running the compiler:

| Existing owner | Relevant boundary |
|---|---|
| [`Program.TrRmExecution`](../../sources/terrumc/Program.TrRmExecution.cs) | Admits configuration before source acquisition, preserves the positional source followed by repeatable `--source` inputs, and calls `Compilation.Create`/`CreateLibrary` without a reference. Provider schema/path pairs are currently artifact-command-only. |
| [`Compilation`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs) | Already has multi-snapshot `CreateLibrary(trees, reference, options)` and `Create(trees, reference, options)` overloads. No new source-binding API is required. |
| [Five-argument `CreateStandardReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs) and [`CreateStandardReferenceSet`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs) | Already expose explicit TypeDef/FieldDef selection and nested-FieldPath reference composition. Their existence does not establish a CLI route or complete feature support. |

This proposal fills only that CLI handoff. Preserve accepted
[source snapshots](../specs/language/source-input.md),
[exact draft agreement](../specs/representation/format-versioning.md),
[admission](../specs/representation/requirements-and-admission.md),
[binding/replacement](../specs/representation/binding-and-replacement.md)
and [consumer trust](../specs/representation/reader-trust.md).
No source grammar, compiler API signature, wire record, Feature, target/runtime
agreement or [`terrum.cli/1`](../specs/cli-protocol.md) amendment is selected.

## Complete version-1 file

```json
{
  "schemaVersion": 1,
  "providers": [
    {
      "schemaIdentity": "trrm/1-draft-init-reference-bodies-local-field-layout-2",
      "path": "pair.trrm"
    },
    {
      "schemaIdentity": "trrm/1-draft-init-reference-bodies-local-field-layout-2",
      "path": "leaf.trrm"
    }
  ],
  "references": [
    {
      "providerInputOrdinal": 0,
      "moduleIdentity": "example.pair",
      "typeExportKeys": ["type.pair"],
      "fieldExportKeys": ["field.pair.inner"]
    },
    {
      "providerInputOrdinal": 1,
      "moduleIdentity": "example.leaf",
      "typeExportKeys": ["type.leaf"],
      "fieldExportKeys": ["field.leaf.value"]
    }
  ]
}
```

The root has exactly `schemaVersion`, `providers`, `references`. Provider
entries have exactly `schemaIdentity`, `path`; reference entries have exactly
the four properties shown. All properties and arrays are required and nonnull.
Both root arrays are nonempty. Each reference requires at least one type key;
its field-key array may be empty. Object order is insignificant; every array
order is retained.

`providers` is the complete ordered artifact input vector, not just artifacts
with explicit lookup roots. A reference's zero-based `providerInputOrdinal`
selects one whole artifact in that vector; `moduleIdentity` selects a Module
**in that artifact**. Several references can select distinct co-located
Modules without resubmitting its bytes. Reject repeated
`(providerInputOrdinal, moduleIdentity)` entries; combine their selections in
one entry instead. `--identity` remains the using/output Module identity,
not a provider selector.

Use one regular UTF-8 JSON file, with at most one leading signature, one
root object and only trailing JSON whitespace. Reject malformed UTF-8,
unpaired decoded surrogates, duplicate decoded properties at every depth
(including escape-equivalent names), unknown/missing properties, nulls,
wrong types, comments, trailing commas/content and nesting beyond **128**.
`schemaVersion` is exactly the number token `1`; ordinals are unsigned
decimal integer tokens without signs, fractions or exponents, fit `Int32`
and are below the provider count. Counts, additions and allocation products
must remain representable under existing checked capacities; no truncation
or partial admission.

All strings are nonempty, NUL-free scalar text. Module identities, keys and
schema literals compare as decoded ordinal text, without trimming,
normalization or case folding. Keys are opaque, not identifiers; whitespace-only
keys remain valid, and their strict UTF-8 encoding must fit the existing
Standard String domain, **1..`0xFF_FFFF` bytes**. Reject a repeated key within
either list or across the two lists of one reference. Distinct explicit
aliases may select one definition. The same key text in different Modules is
not a collision or evidence of equal identity.

Resolve the option operand and every embedded path against the invocation
working directory, **not** the JSON file's directory. The option consumes
exactly one argv element after the required source and before any final
`--json`; reject repetition, empty/missing or dash-leading operands and `-`.
Embedded provider paths are local regular-file paths, never `-`, stdin,
URLs, includes, globs or response files. No extension-based schema selection,
environment expansion or discovery is added.

## Admission, ordering and ownership

1. **CLI selection.** Before configuration content reads, retain the existing
   command, operand, identity, runtime, output-extension and source gates,
   including the ordered maximum of 256 source inputs. Require the exact
   output schema above; omission does not select Development for this option.
   Perform all argv-known path-alias checks before content reads or output
   acquisition. File-identity metadata inspection is not source acquisition.
2. **Configuration preflight.** Read selected configuration files in existing
   argv option order, capturing their contents once. Validate the complete
   provider file, every embedded schema selection and every ordinal before
   reading **any provider or source content**, including stdin. Every provider
   must explicitly select the exact output literal; neither it nor the file's
   `schemaVersion` supplies another artifact's selection. Preserve Core-file
   composition and exact using-Module checks; provider presence designates no
   Core role and does not make unavailable evaluation/context codecs available.
3. **Path identity.** Before provider/source reads, use the existing
   [`PathsAlias`](../../sources/terrumc/Program.PathIdentity.cs) mechanism over
   all resolved source, configuration, provider and output paths. Require
   distinct file inputs across those roles, including duplicate provider-file
   aliases and duplicate source-file aliases. One provider path selected by
   several reference entries is still one input, not a duplicate. Preserve
   case/relative/link/Windows hard-link recognition and existing source/output
   guards; identity-inspection failure is an input failure, not distinctness.
4. **Owned artifacts and references.** Read provider bytes once in vector
   order and retain immutable owned snapshots. For each reference in manifest
   order, call the existing five-argument `CreateStandardReference`: its input
   is the selected vector element, its Module and key lists are exactly those
   supplied, and its providers are **all other vector elements in original
   order**, omitting that input exactly once. Pass empty field lists explicitly;
   never retry a different overload. One result is used directly; two or more
   go through `CreateStandardReferenceSet` in reference-array order. Complete
   reference admission/composition before source acquisition.
5. **Source binding.** Acquire each existing source operand once under
   `source-input/1`, preserving source identity and positional/`--source`
   order. Retain the same immutable syntax-tree vector for binding and every
   local export-map association/hash check; do not concatenate, reorder,
   reread or rebind sources for another reference/map. Invoke `CreateLibrary`
   with the composed reference and existing options when `--library` is set,
   otherwise `Create` with that reference and options. This file adds no source
   selector, source digest or alternative source decoding rule.
6. **Emission and publication.** Emit the one successful checked program with
   the existing exact-schema overload and any independently eligible local
   export map. Reference construction/binding owns imported declaration and
   original-provider associations; emission owns preserving their Imports,
   owner chains, complete contracts and required closure through verification
   and remapping. The CLI must not reconstruct those facts from JSON or names,
   or export imported expectations as new local declarations.
   Refuse missing carriers/unsupported closure rather than
   drop an obligation or retry another schema/emitter.

The reference layer must validate **every supplied artifact and all its
Modules**, including disconnected, declaration-only and unselected inputs,
under the exact selected agreement. They are admitted inputs, not a catalog
from which successful candidates are chosen. Build one unambiguous Module
directory: duplicate Module identities across supplied artifacts reject even
for equal bytes; co-located Modules need no duplicate artifact. Every retained
dependency edge must bind against its original expectation, not merely the
expectations reached by a selected source use. Consumers perform required
structural/support/binding checks, not mandatory semantic re-proof of provider
Bodies.

Type roots and newly selected field bindings require the explicit keys in
their reference entry and the [projection owner's](standard-trrm-layout-queries-source-provider-boundary.md#recommended-consumer-input-and-api)
kind, visibility and exact-owner correspondence: a requested field's owner
must be among that entry's explicitly selected type roots. An external declaring
Module's owner/field selections belong in its own reference entry; provider
presence or a hidden layout node does not select them. Reference construction,
not CLI name lookup or provider search, establishes those associations and
refuses missing/ambiguous selection. Any missing factory enforcement of this
Module/owner scope is a prerequisite, not permission for CLI-side rebinding.
Preserve already-retained exact
provider-owned Import/closure associations; never infer or manufacture keys.
Reference-set reconciliation retains full snapshots and every original view,
under the [composition rules](standard-trrm-layout-queries-reference-composition.md)
as applied to the nested successor, without selecting LayoutQueries query
permissions. For new uses only, preserve explicit first-alias selection order;
order never resolves conflicting Module revisions or ambiguous source names.

Snapshots are immutable **after capture**, not a transaction across changing
files or producer authentication. Replacing a provider on a later invocation
requires a new capture and normal complete binding checks; equal Module/key
text, layout, file paths or hashes do not justify reusing an old decision.

## Failure classes and output preservation

Use the existing experimental human/`--json` projections, not new machine
envelopes or runtime failure categories:

| Failure | Candidate projection |
|---|---|
| Command/option/operand errors, missing/wrong output schema or a provider schema outside the exact version-1 pairing | `TRM2000`, exit 2. Argv failures precede configuration reads; embedded schema failures precede provider/source reads. |
| Configuration/provider/source read or strict-decoding errors; malformed JSON/shape/text/count/ordinal; duplicate reference/key entries; invalid selected key lists, including missing/wrong-kind keys reported for `typeExportKeys`/`fieldExportKeys` | `TRM2001`, exit 2. Identify file and JSON entry/field where attributable. Translate only errors attributable to that input, not arbitrary compiler `ArgumentException`s. |
| Newly checked configuration/provider/input aliases | `TRM2002`, exit 2. Existing source/output argument failures retain `TRM2000`. |
| Syntax/binding diagnostics | Existing source diagnostics, exit 1, with their actual source identities/spans. |
| Invalid/unsupported provider metadata, missing selected Module/dependency, duplicate/conflicting Module snapshots, incompatible expected view, unsupported reference/Body/context closure or existing nested-profile capacity refusal | Existing `TRM5001`, exit 1. No weaker profile, discarded provider or partial reference/artifact. |
| Output publication I/O/access failure | Existing `TRM2001`, exit 2; no success response. |

Do not fabricate source spans for configuration/artifact failures, classify
unexpected host failures as malformed JSON, or import A3-only admission/resource
codes into this route. Keep existing compiler failure distinctions and
runtime-policy refusal paths.

Complete admission, source checks and artifact construction before acquiring
the destination. Recheck every input/configuration/provider versus output alias
immediately before the existing same-directory staged
[`WriteArtifact`](../../sources/terrumc/Program.Compilation.cs) replacement.
All prepublication failures leave an existing destination byte-identical and
create no absent destination. Retain staged-file cleanup; no new crash-durability
or hostile-filesystem transaction protocol is promised.

## Alternatives and adoption

| Alternative | Tradeoff |
|---|---|
| **One shared provider vector plus ordered reference selections — recommended** | One capture per file, explicit Module/key ownership, co-located Modules and cross-provider composition through existing APIs; costs one small configuration grammar and an ordinal indirection. |
| Extend artifact `--trrm-provider` and add per-Module/per-key switches | Still needs explicit source-reference grouping and complete preflight; changes an artifact-only option contract and scatters structured selections across argv. Not selected. |
| Embed dependencies in the type/class export files | Conflates consumer Imports with local publication and broadens two independently owned manifests. Not selected. |

Version 1 deliberately transports only the existing nested type/field route.
No callable-root selector, class/generic/enum composition, provider-source
compilation, package/search path, source alias/rename, inferred name/key,
fallback, schema promotion/conversion, private/native artifact substitution,
Core authority, runtime activation or JIT/AOT support claim is added.

On adoption, owners are `Program` usage, its TRRM configuration/preflight
handler, a separate strict provider-file reader, and the existing reference,
source-binding and emitter handoffs above. Update the CLI inventory when
support changes. No accepted specification or sibling publication proposal
needs amendment for this transport. Option naming, the vector/ordinal shape
and exact nested-only applicability remain explicitly unapproved choices;
implementation must not invent wider pairings from API availability.

### Required future acceptance

| Family | Required cases |
|---|---|
| Basic handoff | Single and multiple source snapshots, library/executable selection, explicit type-only and type+field references, eligible local export-map composition; inspect resulting Module/key/kind and complete imported FieldPath owner chains. |
| Cross-provider | Pair-to-Leaf chain and shared-Leaf diamond; explicit selections from a non-first provider and two co-located Modules. Reuse one actual snapshot while preserving each expected view. Removing a necessary field selection rejects the affected new use, not the whole type's size/identity availability. |
| Exact supplied set | Wrong-schema unused provider rejects before content/source/stdin reads; corrupt, unsupported or dependency-incomplete disconnected provider rejects after capture. Missing dependencies and duplicate Module identities reject, including byte-identical duplicate artifacts at distinct paths. |
| Identity/order | Distinct same-name/same-layout Modules remain distinct; ambiguous source names do not choose a provider. Alias/key/reference order survives composition for new uses; conflicting snapshots reject in either order. Wrong Module/key/kind/field owner, duplicate selections and implicit field/root promotion reject. |
| Replacement/remapping | Through already-admitted source-free APIs, retain the emitted consumer's original expectations while substituting a compatible provider with reordered TypeDef/FieldDef rows; rebuild/re-emit/reload without name-based rebinding. Reject changed owner/type/layout/Copy/access or incompatible provider-owned dependency, including an unused retained expectation. Recompiling source against the replacement is not a substitute for this check. |
| Input/publication safety | Strict JSON/UTF-8/version/integer boundaries, absent/empty/wrong options, out-of-range ordinal, aliases/hard links and late output-alias recheck. For every failure class check diagnostic/status, existing-output bytes, absent-output preservation and no success response. Assert preflight does not consume stdin. |
| Snapshot/isolation | Preserve source ordinals, UTF-16 spans, CRLF/supplementary text and existing map hashes; file changes after capture cannot change retained references or source associations. Existing enum/type/class publication grammars and artifact-command provider options retain their own admission. |

These are implementation gates, not executed tests or evidence of runtime
behavior. This proposal alone closes neither those gates nor the broader
interchange/conformance milestone.
